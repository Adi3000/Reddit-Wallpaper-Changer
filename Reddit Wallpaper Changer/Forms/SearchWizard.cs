using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer.Forms
{
    public partial class SearchWizard : Form
    {
        public string SearchText { get; private set; }

        public SearchWizard(string searchText)
        {
            InitializeComponent();

            searchQuery.Text = searchText;
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://www.reddit.com/wiki/search").Dispose();
        }

        private void searchQuery_TextChanged(object sender, EventArgs e)
        {
            SearchText = searchQuery.Text;
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            if (searchQuery.Text.Contains("nsfw:yes"))
                searchQuery.Text = searchQuery.Text.Replace("nsfw:yes", "nsfw:no");
            else if (searchQuery.Text.Contains("nsfw:no"))
                searchQuery.Text = searchQuery.Text.Replace("nsfw:no", "nsfw:yes");
            else
                searchQuery.Text += " nsfw:no";
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Settings.Default.searchQuery = searchQuery.Text;
            Settings.Default.Save();

            Close();
        }
    }
}
