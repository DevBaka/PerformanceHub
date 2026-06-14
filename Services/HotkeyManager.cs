using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PerformanceHub.Core.Interfaces;

namespace PerformanceHub.Services
{
    public class HotkeyManager : NativeWindow, IHotkeyManager
    {
        private readonly ILogger _log;
        private readonly Dictionary<int, Action> _callbacks = new();

        public HotkeyManager(ILogger log)
        {
            _log = log;
            CreateHandle(new CreateParams());
        }

        public bool Register(int id, Keys modifiers, Keys key, Action callback)
        {
            uint fsModifiers = 0;
            if (modifiers.HasFlag(Keys.Control)) fsModifiers |= 0x0002; // MOD_CONTROL
            if (modifiers.HasFlag(Keys.Alt)) fsModifiers |= 0x0001;     // MOD_ALT
            if (modifiers.HasFlag(Keys.Shift)) fsModifiers |= 0x0004;   // MOD_SHIFT
            if (modifiers.HasFlag(Keys.LWin) || modifiers.HasFlag(Keys.RWin)) fsModifiers |= 0x0008; // MOD_WIN

            if (RegisterHotKey(Handle, id, fsModifiers, (uint)key))
            {
                _callbacks[id] = callback;
                _log.Info($"Registered hotkey id={id}");
                return true;
            }
            else
            {
                _log.Warn($"Failed to register hotkey id={id}");
                return false;
            }
        }

        public void UnregisterAll()
        {
            foreach (var id in _callbacks.Keys)
                UnregisterHotKey(Handle, id);
            _callbacks.Clear();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (_callbacks.TryGetValue(id, out var cb)) cb();
            }
            base.WndProc(ref m);
        }

        // Implement IDisposable to satisfy IHotkeyManager : IDisposable
        public new void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
            GC.SuppressFinalize(this);
        }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
