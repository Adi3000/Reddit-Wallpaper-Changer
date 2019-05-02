using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer.Forms
{
    public sealed partial class Update : Form
    {
        public event EventHandler OnUpdated;

        public Update(string latestVersion)
        {
            InitializeComponent();

            textBox1.Text = latestVersion.Replace("\n", Environment.NewLine);
        }

        //======================================================================
        // Code to run on form load
        //======================================================================
        private void Update_Load(object sender, EventArgs e)
        {
            BringToFront();
            TopMost = true;
        }

        //======================================================================
        // Begin updating RWC
        //======================================================================
        private async void BtnUpdate_Click(object sender, EventArgs e)
        {
            Logger.Instance.LogMessageToFile("Updating Reddit Wallpaper Changer.", LogLevel.Information);

            btnUpdate.Enabled = false;
            progressBar.Visible = true;

            try
            {
                using (var wc = HelperMethods.CreateWebClient())
                {
                    wc.DownloadProgressChanged += (s, a) =>
                    {
                        progressBar.Value = a.ProgressPercentage;
                    };

                    var temp = Path.GetTempPath();

                    var updateSourceUrl = new Uri("https://github.com/qwertydog/Reddit-Wallpaper-Changer/releases/download/release/Reddit.Wallpaper.Changer.msi");

                    var securityVersion = ServicePointManager.SecurityProtocol;
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                    // Download the latest MSI instaler to the users Temp folder
                    await wc.DownloadFileTaskAsync(updateSourceUrl, temp + "Reddit.Wallpaper.Changer.msi");

                    ServicePointManager.SecurityProtocol = securityVersion;

                    Logger.Instance.LogMessageToFile("Update successfully downloaded.", LogLevel.Information);

                    progressBar.Visible = false;

                    try
                    {
                        Logger.Instance.LogMessageToFile("Launching installer and exiting.", LogLevel.Information);

                        Process.Start(temp + "Reddit.Wallpaper.Changer.msi").Dispose();

                        OnUpdated?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        HandleUpdateError(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                HandleUpdateError(ex);
            }
        }

        private static void HandleUpdateError(Exception ex)
        {
            MessageBox.Show("Error Updating: " + ex.Message, "RWC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Logger.Instance.LogMessageToFile("Error Updating: " + ex.Message, LogLevel.Error);
        }
    }
}
