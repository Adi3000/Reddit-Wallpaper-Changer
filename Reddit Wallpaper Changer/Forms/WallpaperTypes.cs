using System;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer.Forms
{
    public partial class WallpaperTypes : Form
    {
        public WallpaperTypes()
        {
            InitializeComponent();
        }

        //======================================================================
        // Close the window
        //======================================================================
        private void btnClose_Click(object sender, EventArgs e) => Close();
    }
}
