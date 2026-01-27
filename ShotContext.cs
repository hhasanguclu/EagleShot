using System;
using System.Drawing;
using System.Windows.Forms;
using EagleShot.Core;
using EagleShot.Forms;

namespace EagleShot
{
    public class ShotContext : ApplicationContext
    {
        private NotifyIcon _trayIcon;
        private HotkeyHandler _hotkeyHandler;

        public ShotContext()
        {
            // Show Splash Screen
            using (var splash = new EagleShot.Forms.SplashForm())
            {
                splash.ShowDialog();
            }

            InitializeTrayIcon();
            InitializeHotkeys();
        }

        private void InitializeTrayIcon()
        {
            Icon appIcon = SystemIcons.Application;
            try 
            {
               if (System.IO.File.Exists("Resources/logo.png"))
               {
                       using(Bitmap original = new Bitmap("Resources/logo.png"))
                       using(Bitmap resized = new Bitmap(original, new Size(64, 64)))
                       {
                           appIcon = Icon.FromHandle(resized.GetHicon());
                       }
               }
            } 
            catch { }

            _trayIcon = new NotifyIcon
            {
                Icon = appIcon,
                Visible = true,
                Text = "EagleShot"
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Take Screenshot", null, (s, e) => ShowOverlay());
            menu.Items.Add("Exit", null, (s, e) => Exit());
            _trayIcon.ContextMenuStrip = menu;
        }

        private void InitializeHotkeys()
        {
            _hotkeyHandler = new HotkeyHandler();
            _hotkeyHandler.HotkeyPressed += (s, e) => ShowOverlay();
            _hotkeyHandler.RegisterPrintScreen();
        }

        private void ShowOverlay()
        {
            if (Application.OpenForms.Count > 0)
            {
               foreach (Form form in Application.OpenForms)
               {
                   if (form is OverlayForm)
                   {
                       form.Activate();
                       return;
                   }
               }
            }

            var overlay = new OverlayForm();
            overlay.ShowDialog();
        }

        private void Exit()
        {
            _trayIcon.Visible = false;
            _hotkeyHandler.Unregister();
            Application.Exit();
        }
    }
}
