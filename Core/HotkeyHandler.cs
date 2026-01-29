using System;
using System.Windows.Forms;

namespace EagleShot.Core
{
    public class HotkeyHandler : Form
    {
        public event EventHandler? HotkeyPressed;

        public HotkeyHandler()
        {
            // Ensure the window is never shown
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Opacity = 0;
            this.Load += (s, e) => this.Hide();
        }

        public bool RegisterPrintScreen()
        {
            // ID 1, No modifiers, PrintScreen
            return NativeMethods.RegisterHotKey(this.Handle, 1, 0, NativeMethods.VK_SNAPSHOT);
        }

        public void Unregister()
        {
            NativeMethods.UnregisterHotKey(this.Handle, 1);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                if (m.WParam.ToInt32() == 1)
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
            }
            base.WndProc(ref m);
        }
    }
}
