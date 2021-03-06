﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
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
            allowed_process_name = new List<Regex>();
            allowed_process_name.Add(new Regex("browser", RegexOptions.Compiled));
            allowed_process_name.Add(new Regex("chrome", RegexOptions.Compiled));
            allowed_process_name.Add(new Regex("opera", RegexOptions.Compiled));
            allowed_process_name.Add(new Regex("nacl64", RegexOptions.Compiled));

            allowed_executables = new List<Regex>();
            allowed_executables.Add(new Regex("browser.exe", RegexOptions.Compiled));
            allowed_executables.Add(new Regex("chrome.exe", RegexOptions.Compiled));
            allowed_executables.Add(new Regex("opera.exe", RegexOptions.Compiled));
            allowed_executables.Add(new Regex("nacl64.exe", RegexOptions.Compiled));
            string[] meaning_regex =
            {
                "gpu", "--type=gpu-process",
                "media", "--type=demuxer-process",
                "plugin", "--type=plugin",
                "plugin", "--type=ppapi",
                "utility", "--type=utility",
                "nacl-broker", "--type=nacl-broker",
                "nacl-loader", "--type=nacl-loader",
                "crashpad", "--type=crashpad-handler",
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
                from p in QueryProcessInfo(true)
                orderby p.ProcessName, p.Meaning, p.Id
                select p
                ).ToList();
            SaveMemoryUsage();
            timerMemUsage.Start();
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
            timerMemUsage.Stop();
            SetEnabled();

            SaveMemoryUsage();

            var processes2 = QueryProcessInfo(false);
            var join = from p2 in processes2
                       from p1 in processes.Where(a => a.Id == p2.Id).DefaultIfEmpty()
                       let Meaning = (p1 == null) ? GetMeaning(GetCommandLine(p2.Id)) : p1.Meaning
                       let TotalProcessorTime1 = (p1 == null) ? new TimeSpan() : p1.TotalProcessorTime
                       orderby p2.ProcessName, Meaning, p2.Id
                       select new
                       {
                           p2.Id,
                           p2.ProcessName,
                           Meaning,
                           TotalMilliseconds = (p2.TotalProcessorTime - TotalProcessorTime1).TotalMilliseconds,
                       };

            var sb = new StringBuilder();
            var delta_time = (stop_at - start_at).TotalMilliseconds;
            string prev_line_process_name = "";
            double total_process_ms = 0.0;
            foreach (var p in join)
            {
                if (prev_line_process_name != "" &&
                    prev_line_process_name != p.ProcessName)
                {
                    sb.AppendFormat("Total [{0}] {1} мс", prev_line_process_name, (Int64)total_process_ms);
                    sb.AppendLine();
                    sb.AppendLine();
                    total_process_ms = 0;
                }
                prev_line_process_name = p.ProcessName;
                total_process_ms += p.TotalMilliseconds;
                MemoryInfo memoryInfo = GetMemoryUsage(p.Id);
                string memUsageString = GetMemoryString(memoryInfo);
                sb.AppendFormat(
                    "{2} {0} ({1}) {3} мс, {4:0.0}%, {5} ",
                    p.ProcessName,
                    p.Meaning,
                    p.Id,
                    (Int64)p.TotalMilliseconds,
                    100 * p.TotalMilliseconds / delta_time,
                    memUsageString
                    );
                sb.AppendLine();
            }
            sb.AppendFormat("Total [{0}] {1} мс", prev_line_process_name, (Int64)total_process_ms);
            textOutput.Text = sb.ToString();
        }

        private string GetMemoryString(MemoryInfo memoryInfo)
        {
            const double bytesPerMb = 1024 * 1024;
            return string.Format("WorkingSet[{0:0.0};{1:0.0};{2:0.0};{3:0.0}], Private[{4:0.0};{5:0.0};{6:0.0};{7:0.0}]",
                memoryInfo.WorkingSetSizeMin / bytesPerMb, 
                memoryInfo.WorkingSetSizeMax / bytesPerMb, 
                memoryInfo.WorkingSetSizeAvg / bytesPerMb,
                memoryInfo.PeakWorkingSetSize/ bytesPerMb,

                memoryInfo.PagefileUsageMin / bytesPerMb,
                memoryInfo.PagefileUsageMax / bytesPerMb,
                memoryInfo.PagefileUsageAvg / bytesPerMb,
                memoryInfo.PeakPagefileUsage/ bytesPerMb
                );
        }

        private MemoryInfo GetMemoryUsage(int id)
        {
            var filtered = (from mem in mem_usages
                    where mem.Id == id
                    select mem).ToArray();

            if (filtered.Count() == 0)
                return new MemoryInfo { };

            return new MemoryInfo
            {
                Id = id,

                WorkingSetSizeAvg = (ulong)filtered.Average(x => (double)x.WorkingSetSize),
                WorkingSetSizeMin = filtered.Min(x => x.WorkingSetSize),
                WorkingSetSizeMax = filtered.Max(x => x.WorkingSetSize),

                PagefileUsageAvg = (ulong)filtered.Average(x => (double)x.PagefileUsage),
                PagefileUsageMin = filtered.Min(x => x.PagefileUsage),
                PagefileUsageMax = filtered.Max(x => x.PagefileUsage),

                PeakPagefileUsage = filtered.Max(x => x.PeakPagefileUsage),
                PeakWorkingSetSize = filtered.Max(x => x.PeakWorkingSetSize),
            };
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

        private List<ProccessInfo> QueryProcessInfo(bool get_command_line)
        {
            var processes = Process.GetProcesses()
               .Where(a => FilterProcess(a))
               .Select(p => TransformProcess(p))
               .Where(p => p.IsValid)
               .ToList();
            foreach(var p in processes)
            {
                if (get_command_line)
                {
                    p.CommandLine = GetCommandLine(p.Id);
                    p.Meaning = GetMeaning(p.CommandLine);
                }
            }
            return processes;
        }
        private bool FilterProcess(Process process)
        {
            try
            {
                var ProcessName = process.ProcessName;
                bool process_name_match = false;
                foreach (var f in allowed_process_name)
                {
                    if (f.IsMatch(ProcessName))
                    {
                        process_name_match = true;
                        break;
                    }
                }
                if (!process_name_match)
                    return false;
                var FileName = process.MainModule.FileName;
                foreach (var f in allowed_executables)
                {
                    if (f.IsMatch(FileName))
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
                    Handle = process.Handle,
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
            public IntPtr Handle { get; set; }
            public string ProcessName { get; set; }
            public string CommandLine { get; set; }
            public string FileName { get; set; }

            public string Meaning { get; set; }
        }
        class MemoryInfoSample
        {
            public int Id { get; set; }
            public ulong WorkingSetSize { get; set; }
            public ulong PagefileUsage { get; set; }
            public ulong PeakPagefileUsage { get; set; }
            public ulong PeakWorkingSetSize { get; set; }
        }
        class MemoryInfo
        {
            public int Id { get; set; }

            public ulong WorkingSetSizeMin { get; set; }
            public ulong WorkingSetSizeMax { get; set; }
            public ulong WorkingSetSizeAvg { get; set; }

            public ulong PagefileUsageMin { get; set; }
            public ulong PagefileUsageMax { get; set; }
            public ulong PagefileUsageAvg { get; set; }

            public ulong PeakPagefileUsage { get; set; }
            public ulong PeakWorkingSetSize { get; set; }
        }

        private DateTime start_at;
        private DateTime stop_at;
        private List<ProccessInfo> processes;
        private List<MemoryInfoSample> mem_usages = new List<MemoryInfoSample>();

        private static List<Regex> allowed_process_name;
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

        private void timerMemUsage_Tick(object sender, EventArgs e)
        {
            SaveMemoryUsage();
        }

        private void SaveMemoryUsage()
        {
            foreach (var p in processes)
            {
                WinApiHelpers.PROCESS_MEMORY_COUNTERS counters;
                counters.cb = (uint)Marshal.SizeOf(typeof(WinApiHelpers.PROCESS_MEMORY_COUNTERS));
                if (!WinApiHelpers.GetProcessMemoryInfo(p.Handle, out counters, counters.cb))
                    continue;
                MemoryInfoSample mem_info = new MemoryInfoSample { Id = p.Id };
                mem_info.WorkingSetSize = counters.WorkingSetSize;
                mem_info.PagefileUsage = counters.PagefileUsage;
                mem_info.PeakPagefileUsage = counters.PeakPagefileUsage;
                mem_info.PeakWorkingSetSize = counters.PeakWorkingSetSize;
                mem_usages.Add(mem_info);
            }
        }
    }
}
