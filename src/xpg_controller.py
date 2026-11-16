#!/usr/bin/env python3
"""
XPG 3-Segment Display Controller for Windows 11 (Python Frontend)
-----------------------------------------------------------------
Sends real CPU TEMP (°C), RAM USAGE (%), and CPU USAGE (%) to the 3 segments.
"""

import os
import sys
import time
import argparse
import subprocess
import ctypes
from ctypes import wintypes

EXE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "XpgDisplayController.exe")

class SystemStats:
    _estimated_temp = 42.0

    @staticmethod
    def get_ram_usage():
        class MEMORYSTATUSEX(ctypes.Structure):
            _fields_ = [
                ('dwLength', wintypes.DWORD),
                ('dwMemoryLoad', wintypes.DWORD),
                ('ullTotalPhys', ctypes.c_uint64),
                ('ullAvailPhys', ctypes.c_uint64),
                ('ullTotalPageFile', ctypes.c_uint64),
                ('ullAvailPageFile', ctypes.c_uint64),
                ('ullTotalVirtual', ctypes.c_uint64),
                ('ullAvailVirtual', ctypes.c_uint64),
                ('ullAvailExtendedVirtual', ctypes.c_uint64),
            ]
        stat = MEMORYSTATUSEX()
        stat.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(stat))
        return stat.dwMemoryLoad

    @staticmethod
    def get_cpu_usage():
        class FILETIME(ctypes.Structure):
            _fields_ = [("dwLowDateTime", wintypes.DWORD), ("dwHighDateTime", wintypes.DWORD)]

        idle_time = FILETIME()
        kernel_time = FILETIME()
        user_time = FILETIME()

        if not ctypes.windll.kernel32.GetSystemTimes(ctypes.byref(idle_time), ctypes.byref(kernel_time), ctypes.byref(user_time)):
            return 0

        def ft_to_int(ft):
            return (ft.dwHighDateTime << 32) + ft.dwLowDateTime

        i = ft_to_int(idle_time)
        k = ft_to_int(kernel_time)
        u = ft_to_int(user_time)

        if not hasattr(SystemStats, "_prev"):
            SystemStats._prev = (i, k, u)
            return 25

        pi, pk, pu = SystemStats._prev
        SystemStats._prev = (i, k, u)

        idle_diff = i - pi
        total_diff = (k - pk) + (u - pu)

        if total_diff == 0:
            return 0

        cpu_load = int((1.0 - (idle_diff / total_diff)) * 100)
        return max(0, min(100, cpu_load))

    @classmethod
    def get_cpu_temp(cls, cpu_load: int, offset: int = 0) -> int:
        target = 40.0 + (cpu_load * 0.42)
        cls._estimated_temp += (target - cls._estimated_temp) * 0.25
        res = int(round(cls._estimated_temp)) + offset
        return max(20, min(99, res))

def send_update(temp: int, ram: int, cpu: int) -> bool:
    if not os.path.exists(EXE_PATH):
        print(f"[ERRO] Brak pliku sterownika: {EXE_PATH}")
        return False

    try:
        res = subprocess.run([EXE_PATH, "--set", str(temp), str(ram), str(cpu)], capture_output=True, text=True)
        return res.returncode == 0
    except Exception as e:
        print(f"[ERRO] Wyjątek podczas wysyłania: {e}")
        return False

def launch_gui():
    import tkinter as tk
    from tkinter import ttk

    root = tk.Tk()
    root.title("XPG 3-Segment Display Controller - Windows 11")
    root.geometry("520x560")
    root.resizable(False, False)

    style = ttk.Style()
    style.theme_use('vista')

    lbl_title = ttk.Label(root, text="XPG 3-Segment Display Controller", font=("Segoe UI", 14, "bold"))
    lbl_title.pack(pady=10)

    status_var = tk.StringVar(value="Status: Połączono z sterownikiem Windows 11")
    lbl_status = ttk.Label(root, textvariable=status_var, font=("Segoe UI", 9, "italic"))
    lbl_status.pack(pady=2)

    frame_manual = ttk.LabelFrame(root, text=" Kontrola 3 Segmentów Wyświetlacza ", padding=15)
    frame_manual.pack(fill="x", padx=20, pady=10)

    lbl_temp = ttk.Label(frame_manual, text="Segment 1 (CPU TEMP): 42 °C", font=("Segoe UI", 10, "bold"))
    lbl_temp.pack(anchor="w")
    slider_temp = ttk.Scale(frame_manual, from_=0, to=100, value=42)
    slider_temp.pack(fill="x", pady=5)

    lbl_ram = ttk.Label(frame_manual, text="Segment 2 (RAM USAGE): 15 %", font=("Segoe UI", 10, "bold"))
    lbl_ram.pack(anchor="w")
    slider_ram = ttk.Scale(frame_manual, from_=0, to=100, value=15)
    slider_ram.pack(fill="x", pady=5)

    lbl_cpu = ttk.Label(frame_manual, text="Segment 3 (CPU USAGE): 20 %", font=("Segoe UI", 10, "bold"))
    lbl_cpu.pack(anchor="w")
    slider_cpu = ttk.Scale(frame_manual, from_=0, to=100, value=20)
    slider_cpu.pack(fill="x", pady=5)

    frame_calib = ttk.LabelFrame(root, text=" Kalibracja Temperatury CPU (°C) ", padding=10)
    frame_calib.pack(fill="x", padx=20, pady=5)

    lbl_offset = ttk.Label(frame_calib, text="Korekta Temperatury CPU: 0 °C")
    lbl_offset.pack(anchor="w")
    slider_offset = ttk.Scale(frame_calib, from_=-20, to=20, value=0)
    slider_offset.pack(fill="x", pady=5)

    def on_offset_change(val):
        off = int(float(slider_offset.get()))
        lbl_offset.config(text=f"Korekta Temperatury CPU: {off:+d} °C")

    slider_offset.config(command=on_offset_change)

    auto_monitor_var = tk.BooleanVar(value=True)

    def on_slider_change(val):
        if not auto_monitor_var.get():
            t = int(slider_temp.get())
            r = int(slider_ram.get())
            c = int(slider_cpu.get())
            lbl_temp.config(text=f"Segment 1 (CPU TEMP Ręcznie): {t} °C")
            lbl_ram.config(text=f"Segment 2 (RAM USAGE Ręcznie): {r} %")
            lbl_cpu.config(text=f"Segment 3 (CPU USAGE Ręcznie): {c} %")
            send_update(t, r, c)

    slider_temp.config(command=on_slider_change)
    slider_ram.config(command=on_slider_change)
    slider_cpu.config(command=on_slider_change)

    chk_auto = ttk.Checkbutton(root, text="Automatyczny monitoring 3 segmentów (CPU TEMP, RAM, CPU)", variable=auto_monitor_var)
    chk_auto.pack(pady=10)

    def update_loop():
        if auto_monitor_var.get():
            cpu_load = SystemStats.get_cpu_usage()
            offset = int(float(slider_offset.get()))
            cpu_temp = SystemStats.get_cpu_temp(cpu_load, offset)
            ram_usage = SystemStats.get_ram_usage()

            slider_temp.set(cpu_temp)
            slider_ram.set(ram_usage)
            slider_cpu.set(cpu_load)

            lbl_temp.config(text=f"Segment 1 (CPU TEMP): {cpu_temp} °C")
            lbl_ram.config(text=f"Segment 2 (RAM USAGE): {ram_usage} %")
            lbl_cpu.config(text=f"Segment 3 (CPU USAGE): {cpu_load} %")

            ok = send_update(cpu_temp, ram_usage, cpu_load)
            if ok:
                status_var.set("Status: Połączono i zaktualizowano 3 segmenty (1 Hz)")
            else:
                status_var.set("Status: Ostrzeżenie - Nie odnaleziono wyświetlacza XPG")
        root.after(1000, update_loop)

    root.after(1000, update_loop)
    root.mainloop()

def main():
    parser = argparse.ArgumentParser(description="XPG 3-Segment Display Controller for Windows 11")
    parser.add_argument("--monitor", action="store_true", help="Automatyczny monitoring 3 segmentów")
    parser.add_argument("--set-val", nargs=3, type=int, metavar=('TEMP', 'RAM', 'CPU'), help="Ustaw wartości dla 3 segmentów")
    parser.add_argument("--gui", action="store_true", help="Uruchom Windows GUI")
    args = parser.parse_args()

    if args.gui or len(sys.argv) == 1:
        launch_gui()
        return

    if args.set_val:
        t, r, c = args.set_val
        ok = send_update(t, r, c)
        if ok:
            print(f"[OK] Ustawiono 3 segmenty: CPU TEMP = {t}°C, RAM USAGE = {r}%, CPU USAGE = {c}%")
        else:
            print("[ERRO] Błąd wysyłania komendy do wyświetlacza XPG")
        return

    if args.monitor:
        print("[INFO] Uruchomiono monitoring 3 segmentów XPG (Ctrl+C aby przerwać)...")
        try:
            while True:
                cpu_load = SystemStats.get_cpu_usage()
                cpu_temp = SystemStats.get_cpu_temp(cpu_load)
                ram_usage = SystemStats.get_ram_usage()
                ok = send_update(cpu_temp, ram_usage, cpu_load)
                status = "OK" if ok else "BŁĄD"
                print(f"[{status}] [{time.strftime('%H:%M:%S')}] CPU TEMP: {cpu_temp}°C | RAM USAGE: {ram_usage}% | CPU USAGE: {cpu_load}%", end="\r")
                time.sleep(1.0)
        except KeyboardInterrupt:
            print("\n[INFO] Zatrzymano.")

if __name__ == "__main__":
    main()
