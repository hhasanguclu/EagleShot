using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EagleShot.Forms
{
    public class SplashForm : Form
    {
        public SplashForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(400, 400); // Adjust based on image aspect ratio
            this.ShowInTaskbar = false;
            
            try
            {
                // Basic way to make it non-rectangular if needed, but for now just image
                // If using TransparencyKey, make sure background matches
                this.BackgroundImage = new Bitmap("Resources/splash_logo.jpg");
                this.BackgroundImageLayout = ImageLayout.Zoom;
                this.BackColor = Color.Black; 
                
                // If the user wants part of it transparent, we can set TransparencyKey
                // this.TransparencyKey = Color.Black; 
            }
            catch { }
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            
            // Wait 2 seconds
            await Task.Delay(2000);
            
            // Fade out
            while (this.Opacity > 0)
            {
                this.Opacity -= 0.05;
                await Task.Delay(20);
            }
            
            this.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Optional: Draw a border or text if needed
        }
    }
}
