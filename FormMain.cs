using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CpuTime
{
    public partial class FormMain : Form
    {
        static FormMain()
        {
            allowed_executables = new List<Regex>();
            allowed_executables.Add(new Regex("browser.exe"));
            allowed_executables.Add(new Regex("chrome.exe"));
            allowed_executables.Add(new Regex("opera.exe"));
            allowed_executables.Add(new Regex("nacl64.exe"));
            string[] meaning_regex =
            {
                "gpu", "--type=gpu-process",
                "media", "--type=demuxer-process",
                "plugin", "--type=plugin",
                "utility", "--type=utility",
                "nacl-broker", "--type=nacl-broker",
                "nacl-loader", "--type=nacl-loader",
                "renderer", "--type=renderer",
                "main", "",
            };
            meanings = new List<MeaningAndFilter>();
            for (int i = 0; i < meaning_regex.Count(); i += 2)
            {
                meanings.Add(new MeaningAndFilter
                {
                    Meaning = meaning_regex[i],
                    Filter = new Regex(meaning_regex[i + 1])
                });
            }
        }

        public FormMain()
        {
            InitializeComponent();
            lCurrentTime.Text = "";
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            start_at = DateTime.Now;
            timer1.Start();
            processes = (
                from p in QueryProcessInfo()
                orderby p.ProcessName, p.Id
                select p
                ).ToList();
            SetEnabled();
            var sb = new StringBuilder();
            foreach (var p in processes)
            {
                sb.AppendFormat(
                    "{2} {0} ({1})",
                    p.ProcessName,
                    p.Meaning,
                    p.Id
                    );
                sb.AppendLine();
            }
            textOutput.Text = sb.ToString();
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            stop_at = DateTime.Now;
            timer1.Stop();
            SetEnabled();

            var processes2 = QueryProcessInfo();

            var join = from p2 in processes2
                       from p1 in processes.Where(a => a.Id == p2.Id).DefaultIfEmpty()
                       orderby p2.ProcessName, p2.Id
                       let Meaning = (p1 == null) ? GetMeaning(GetCommandLine(p2.Id)) : p1.Meaning
                       let TotalProcessorTime1 = (p1 == null) ? new TimeSpan() : p1.TotalProcessorTime
                       select new
                       {
                           p2.Id,
                           p2.ProcessName,
                           Meaning,
                           TotalMilliseconds = (p2.TotalProcessorTime - TotalProcessorTime1).TotalMilliseconds
                       };

            var sb = new StringBuilder();
            var delta_time = (stop_at - start_at).TotalMilliseconds;
            foreach (var p in join)
            {
                sb.AppendFormat(
                    "{2} {0} ({1}) {3} мс, {4:0.0}%",
                    p.ProcessName,
                    p.Meaning,
                    p.Id,
                    (Int64)p.TotalMilliseconds,
                    100 * p.TotalMilliseconds / delta_time
                    );
                sb.AppendLine();
            }
            textOutput.Text = sb.ToString();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            var time_spend = (DateTime.Now - start_at).TotalSeconds;
            var text = string.Format("Начали замер в {0:HH:mm:ss}, прошло {1:0.0} с",
                start_at, time_spend);
            lCurrentTime.Text = text;
            if (udDelay.Value != 0 && time_spend >= (double)udDelay.Value)
                btnStop_Click(btnStop, null);
        }

        private List<ProccessInfo> QueryProcessInfo()
        {
            var processes = Process.GetProcesses()
               .Where(a => FilterProcess(a))
               .Select(p => TransformProcess(p))
               .Where(p => p.IsValid)
               .ToList();
            foreach(var p in processes)
            {
                p.CommandLine = GetCommandLine(p.Id);
                p.Meaning = GetMeaning(p.CommandLine);
            }
            return processes;
        }
        private bool FilterProcess(Process process)
        {
            try
            {
                foreach (var f in allowed_executables)
                {
                    if (f.IsMatch(process.MainModule.FileName))
                        return true;
                }
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            return false;
        }

        private ProccessInfo TransformProcess(Process process)
        {
            try
            {
                return new ProccessInfo
                {
                    IsValid = true,
                    Id = process.Id,
                    ProcessName = process.ProcessName,
                    FileName = process.MainModule.FileName,
                    TotalProcessorTime = process.TotalProcessorTime,
                };
            }
            catch (InvalidOperationException)
            {
            }
            return new ProccessInfo();
        }
        private string GetCommandLine(int processId)
        {
            var commandLine = new StringBuilder();
            using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId))
            {
                foreach (var @object in searcher.Get())
                {
                    commandLine.Append(@object["CommandLine"] + " ");
                }
            }
            return commandLine.ToString();
        }

        private string GetMeaning(string command_line)
        {
            foreach (var m in meanings)
            {
                if (m.Filter.IsMatch(command_line))
                    return m.Meaning;
            }
            return "";
        }

        class MeaningAndFilter
        {
            public string Meaning { get; set; }
            public Regex Filter { get; set; }
        }
        class ProccessInfo
        {
            public bool IsValid { get; set; }
            public TimeSpan TotalProcessorTime { get; set; }
            public int Id { get; set; }
            public string ProcessName { get; set; }
            public string CommandLine { get; set; }
            public string FileName { get; set; }

            public string Meaning { get; set; }
        }

        private DateTime start_at;
        private DateTime stop_at;
        private List<ProccessInfo> processes;

        private static List<Regex> allowed_executables;
        private static List<MeaningAndFilter> meanings;

        void SetEnabled()
        {
            btnStop.Enabled = timer1.Enabled;
            udDelay.Enabled = !timer1.Enabled;
        }
        private void udDelay_ValueChanged(object sender, EventArgs e)
        {
            SetEnabled();
            if (udDelay.Value == 0)
                btnStop.Text = "Стоп";
            else
                btnStop.Text = string.Format("Стоп {0} c.", udDelay.Value);
        }
    }
}
