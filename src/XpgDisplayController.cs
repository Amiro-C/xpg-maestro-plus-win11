using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace XpgController
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool startMinimized = false;

            if (args.Length > 0)
            {
                if (args[0] == "--set" && args.Length >= 4)
                {
                    byte temp = byte.Parse(args[1]);
                    byte ram = byte.Parse(args[2]);
                    byte cpu = byte.Parse(args[3]);
                    bool ok = XpgDriver.SendUpdate(temp, ram, cpu);
                    Console.WriteLine(ok ? string.Format("[OK] Sent CPU Temp = {0}°C, RAM Usage = {1}%, CPU Usage = {2}%", temp, ram, cpu) : "[ERROR] Failed to send HID report");
                    return;
                }
                else if (args[0] == "--monitor")
                {
                    Console.WriteLine("[INFO] Starting XPG 3-Segment Monitor (CPU Temp, RAM Usage, CPU Usage)...");
                    RunMonitorConsole();
                    return;
                }
                else if (args[0] == "--minimized" || args[0] == "-m")
                {
                    startMinimized = true;
                }
            }

            // Launch GUI
            Application.Run(new MainForm(startMinimized));
        }

        private static void RunMonitorConsole()
        {
            var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();

            while (true)
            {
                Thread.Sleep(1000);
                byte cpuLoad = (byte)Math.Min(100, Math.Max(0, (int)cpuCounter.NextValue()));
                byte cpuTemp = (byte)Math.Min(99, Math.Max(20, CpuTemperatureProvider.GetCpuTemperature(cpuLoad, 0)));
                byte ramUsage = (byte)Math.Min(100, Math.Max(0, Telemetry.GetRamUsage()));

                bool ok = XpgDriver.SendUpdate(cpuTemp, ramUsage, cpuLoad);
                string status = ok ? "OK" : "ERROR";
                Console.WriteLine(string.Format("[{0}] [{1}] CPU Temp: {2}°C | RAM Usage: {3}% | CPU Usage: {4}%", status, DateTime.Now.ToString("HH:mm:ss"), cpuTemp, ramUsage, cpuLoad));
            }
        }
    }

    public static class AutostartManager
    {
        private const string REG_KEY_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "XpgDisplayController";

        public static bool IsAutostartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_KEY_PATH, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(APP_NAME);
                        return val != null;
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool SetAutostart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_KEY_PATH, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Application.ExecutablePath;
                            key.SetValue(APP_NAME, string.Format("\"{0}\" --minimized", exePath));
                        }
                        else
                        {
                            key.DeleteValue(APP_NAME, false);
                        }
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }

    public static class CpuTemperatureProvider
    {
        private static double currentEstimatedTemp = 42.0;

        public static int GetCpuTemperature(int cpuLoadPercentage, int offsetDegrees = 0)
        {
            // 1. Try LibreHardwareMonitor WMI
            int ohmTemp = TryGetWmiSensorTemp(@"root\LibreHardwareMonitor");
            if (ohmTemp <= 0)
            {
                // 2. Try OpenHardwareMonitor WMI
                ohmTemp = TryGetWmiSensorTemp(@"root\OpenHardwareMonitor");
            }

            if (ohmTemp > 20 && ohmTemp < 115)
            {
                return Math.Min(99, Math.Max(20, ohmTemp + offsetDegrees));
            }

            // 3. Dynamic Thermal Model (CPU Heatsink Thermal Inertia Physics)
            double targetTemp = 40.0 + (cpuLoadPercentage * 0.42);
            currentEstimatedTemp += (targetTemp - currentEstimatedTemp) * 0.25;

            int calculatedTemp = (int)Math.Round(currentEstimatedTemp) + offsetDegrees;
            return Math.Min(99, Math.Max(20, calculatedTemp));
        }

        private static int TryGetWmiSensorTemp(string wmiNamespace)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(wmiNamespace, "SELECT Value, Name FROM Sensor WHERE SensorType = 'Temperature' AND (Name LIKE '%CPU Package%' OR Name LIKE '%CPU Total%' OR Name LIKE '%CPU Core%')"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (obj["Value"] != null)
                        {
                            float val = Convert.ToSingle(obj["Value"]);
                            if (val > 20 && val < 115) return (int)Math.Round(val);
                        }
                    }
                }
            }
            catch { }
            return 0;
        }
    }

    public static class Telemetry
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static int GetRamUsage()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return (int)memStatus.dwMemoryLoad;
            }
            return 0;
        }
    }

    public static class XpgDriver
    {
        public const ushort VENDOR_ID = 0x125F;  // ADATA / XPG
        public const ushort PRODUCT_ID = 0x7601; // XPG Segment Display
        public const byte REPORT_ID = 0x07;       // Report ID 7

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid HidGuid);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        public static string FindDevicePath()
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x10);
            if (deviceInfoSet == (IntPtr)(-1)) return null;

            SP_DEVICE_INTERFACE_DATA interfaceData = new SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

            for (uint index = 0; SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData); index++)
            {
                uint requiredSize = 0;
                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
                IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);

                if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailDataBuffer, requiredSize, out requiredSize, IntPtr.Zero))
                {
                    IntPtr pDevicePath = new IntPtr(detailDataBuffer.ToInt64() + 4);
                    string path = Marshal.PtrToStringAuto(pDevicePath);
                    Marshal.FreeHGlobal(detailDataBuffer);

                    if (path != null && path.ToLower().Contains("vid_125f") && path.ToLower().Contains("pid_7601"))
                    {
                        SetupDiDestroyDeviceInfoList(deviceInfoSet);
                        return path;
                    }
                }
                else
                {
                    Marshal.FreeHGlobal(detailDataBuffer);
                }
            }
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
            return null;
        }

        public static bool SendUpdate(byte cpuTemp, byte ramUsage, byte cpuUsage)
        {
            string path = FindDevicePath();
            if (string.IsNullOrEmpty(path)) return false;

            using (SafeFileHandle handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return false;

                byte[] report = new byte[65]; // Report ID + 64 bytes payload
                report[0] = REPORT_ID;  // 0x07
                report[1] = 0xA1;       // Command Magic
                report[2] = 0x01;       // Version / Mode
                report[6] = 0x0A;       // Marker 1
                report[7] = cpuTemp;    // SEGMENT 1: CPU TEMP (°C)
                report[8] = 0x0A;       // Marker 2
                report[9] = ramUsage;   // SEGMENT 2: RAM USAGE (%)
                report[10] = cpuUsage;  // SEGMENT 3: CPU USAGE (%)
                report[11] = 0x07;      // Flag
                report[12] = 0x07;      // Flag
                report[13] = 0x07;      // Flag

                return HidD_SetOutputReport(handle, report, (uint)report.Length);
            }
        }
    }

    public class MainForm : Form
    {
        private Label lblTitle;
        private Label lblStatus;
        private GroupBox grpSegments;
        private Label lblTemp;
        private TrackBar trkTemp;
        private Label lblRam;
        private TrackBar trkRam;
        private Label lblCpu;
        private TrackBar trkCpu;
        private GroupBox grpCalib;
        private Label lblOffset;
        private TrackBar trkOffset;
        private GroupBox grpSettings;
        private CheckBox chkAutoMonitor;
        private CheckBox chkCloseToTray;
        private CheckBox chkAutostart;
        private Button btnHideToTray;
        private System.Windows.Forms.Timer timerMonitor;
        private PerformanceCounter cpuCounter;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool isExplicitExit = false;
        private bool startMinimized = false;

        public MainForm(bool startMinimized = false)
        {
            this.startMinimized = startMinimized;
            InitializeComponent();
            InitPerformanceCounter();
        }

        private void InitPerformanceCounter()
        {
            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.Text = "XPG 3-Segment Display Controller - Windows 11";
            this.Size = new Size(530, 640);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            lblTitle = new Label
            {
                Text = "XPG 3-Segment Display Controller",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };

            lblStatus = new Label
            {
                Text = "Status: Sprawdzanie połączenia USB...",
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.DarkSlateGray,
                Location = new Point(22, 48),
                AutoSize = true
            };

            grpSegments = new GroupBox
            {
                Text = " Wartości 3 Segmentów Wyświetlacza ",
                Font = new Font("Segoe UI", 9),
                Location = new Point(20, 75),
                Size = new Size(470, 245)
            };

            // Segment 1: CPU TEMP
            lblTemp = new Label
            {
                Text = "Segment 1 (CPU TEMP): 42 °C",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(15, 22),
                AutoSize = true
            };

            trkTemp = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 42,
                Location = new Point(15, 45),
                Size = new Size(435, 45),
                TickFrequency = 10
            };
            trkTemp.ValueChanged += OnTrackBarChanged;

            // Segment 2: RAM USAGE
            lblRam = new Label
            {
                Text = "Segment 2 (RAM USAGE): 15 %",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(15, 95),
                AutoSize = true
            };

            trkRam = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 15,
                Location = new Point(15, 118),
                Size = new Size(435, 45),
                TickFrequency = 10
            };
            trkRam.ValueChanged += OnTrackBarChanged;

            // Segment 3: CPU USAGE
            lblCpu = new Label
            {
                Text = "Segment 3 (CPU USAGE): 20 %",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(15, 168),
                AutoSize = true
            };

            trkCpu = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 20,
                Location = new Point(15, 190),
                Size = new Size(435, 45),
                TickFrequency = 10
            };
            trkCpu.ValueChanged += OnTrackBarChanged;

            grpSegments.Controls.Add(lblTemp);
            grpSegments.Controls.Add(trkTemp);
            grpSegments.Controls.Add(lblRam);
            grpSegments.Controls.Add(trkRam);
            grpSegments.Controls.Add(lblCpu);
            grpSegments.Controls.Add(trkCpu);

            // Kalibracja Temperatury CPU
            grpCalib = new GroupBox
            {
                Text = " Kalibracja Temperatury CPU (°C) ",
                Font = new Font("Segoe UI", 9),
                Location = new Point(20, 330),
                Size = new Size(470, 95)
            };

            lblOffset = new Label
            {
                Text = "Korekta Temperatury CPU: 0 °C",
                Location = new Point(15, 22),
                AutoSize = true
            };

            trkOffset = new TrackBar
            {
                Minimum = -20,
                Maximum = 20,
                Value = 0,
                Location = new Point(15, 42),
                Size = new Size(435, 45),
                TickFrequency = 5
            };
            trkOffset.ValueChanged += (s, e) => {
                lblOffset.Text = string.Format("Korekta Temperatury CPU: {0:+0;-0;0} °C", trkOffset.Value);
            };

            grpCalib.Controls.Add(lblOffset);
            grpCalib.Controls.Add(trkOffset);

            // Opcje Systemowe (Autostart & Zasobnik Systemowy)
            grpSettings = new GroupBox
            {
                Text = " Ustawienia Zasobnika Systemowego i Autostartu ",
                Font = new Font("Segoe UI", 9),
                Location = new Point(20, 435),
                Size = new Size(470, 145)
            };

            chkAutoMonitor = new CheckBox
            {
                Text = "Automatyczny monitoring (CPU TEMP, RAM, CPU)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Checked = true,
                Location = new Point(15, 22),
                AutoSize = true
            };

            chkCloseToTray = new CheckBox
            {
                Text = "Zamknięcie okna (X) ukrywa program w zasobniku",
                Font = new Font("Segoe UI", 9f),
                Checked = true,
                Location = new Point(15, 48),
                AutoSize = true
            };

            chkAutostart = new CheckBox
            {
                Text = "Uruchamiaj z systemem Windows 11 (Autostart)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Checked = AutostartManager.IsAutostartEnabled(),
                Location = new Point(15, 74),
                AutoSize = true
            };
            chkAutostart.CheckedChanged += OnAutostartChanged;

            btnHideToTray = new Button
            {
                Text = "Ukryj w Zasobniku Systemowym",
                Font = new Font("Segoe UI", 9f),
                Location = new Point(15, 103),
                Size = new Size(435, 30)
            };
            btnHideToTray.Click += (s, e) => HideToTray();

            grpSettings.Controls.Add(chkAutoMonitor);
            grpSettings.Controls.Add(chkCloseToTray);
            grpSettings.Controls.Add(chkAutostart);
            grpSettings.Controls.Add(btnHideToTray);

            this.Controls.Add(lblTitle);
            this.Controls.Add(lblStatus);
            this.Controls.Add(grpSegments);
            this.Controls.Add(grpCalib);
            this.Controls.Add(grpSettings);

            // Context Menu dla Zasobnika
            trayMenu = new ContextMenuStrip();
            var menuShow = new ToolStripMenuItem("Pokaż Panel XPG", null, (s, e) => RestoreFromTray());
            var menuMonitor = new ToolStripMenuItem("Monitoring włączony", null, (s, e) => {
                chkAutoMonitor.Checked = !chkAutoMonitor.Checked;
            });
            var menuExit = new ToolStripMenuItem("Zamknij Program", null, (s, e) => {
                isExplicitExit = true;
                Application.Exit();
            });

            trayMenu.Items.Add(menuShow);
            trayMenu.Items.Add(menuMonitor);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(menuExit);

            // Ikona Zasobnika Systemowego
            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "XPG 3-Segment Display Controller",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            // Setup 1 Hz Timer
            timerMonitor = new System.Windows.Forms.Timer();
            timerMonitor.Interval = 1000;
            timerMonitor.Tick += OnTimerTick;
            timerMonitor.Start();

            this.Resize += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Minimized)
                {
                    HideToTray();
                }
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (startMinimized)
            {
                BeginInvoke(new Action(() => HideToTray()));
            }
        }

        private void OnAutostartChanged(object sender, EventArgs e)
        {
            bool ok = AutostartManager.SetAutostart(chkAutostart.Checked);
            if (!ok)
            {
                MessageBox.Show("Nie udało się zaktualizować rejestru autostartu Windows.", "Błąd Autostartu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void HideToTray()
        {
            this.Hide();
            this.ShowInTaskbar = false;
            trayIcon.ShowBalloonTip(2000, "XPG Display Controller", "Program działa w tle i aktualizuje wyświetlacz.", ToolTipIcon.Info);
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void OnTrackBarChanged(object sender, EventArgs e)
        {
            if (!chkAutoMonitor.Checked)
            {
                lblTemp.Text = string.Format("Segment 1 (CPU TEMP): {0} °C", trkTemp.Value);
                lblRam.Text = string.Format("Segment 2 (RAM USAGE): {0} %", trkRam.Value);
                lblCpu.Text = string.Format("Segment 3 (CPU USAGE): {0} %", trkCpu.Value);
                XpgDriver.SendUpdate((byte)trkTemp.Value, (byte)trkRam.Value, (byte)trkCpu.Value);
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (chkAutoMonitor.Checked)
            {
                byte cpuLoad = 0;
                if (cpuCounter != null)
                {
                    try { cpuLoad = (byte)Math.Min(100, Math.Max(0, (int)cpuCounter.NextValue())); } catch { }
                }

                byte cpuTemp = (byte)Math.Min(99, Math.Max(20, CpuTemperatureProvider.GetCpuTemperature(cpuLoad, trkOffset.Value)));
                byte ramUsage = (byte)Math.Min(100, Math.Max(0, Telemetry.GetRamUsage()));

                trkTemp.Value = Math.Min(100, (int)cpuTemp);
                trkRam.Value = Math.Min(100, (int)ramUsage);
                trkCpu.Value = Math.Min(100, (int)cpuLoad);

                lblTemp.Text = string.Format("Segment 1 (CPU TEMP): {0} °C", cpuTemp);
                lblRam.Text = string.Format("Segment 2 (RAM USAGE): {0} %", ramUsage);
                lblCpu.Text = string.Format("Segment 3 (CPU USAGE): {0} %", cpuLoad);

                bool ok = XpgDriver.SendUpdate(cpuTemp, ramUsage, cpuLoad);
                lblStatus.Text = ok ? "Status: Połączono z wyświetlaczem XPG (Aktualizacja 1 Hz)" : "Status: Ostrzeżenie - Nie odnaleziono urządzenia XPG";
                lblStatus.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!isExplicitExit && chkCloseToTray.Checked && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
            else
            {
                if (trayIcon != null) trayIcon.Dispose();
                base.OnFormClosing(e);
            }
        }
    }
}
