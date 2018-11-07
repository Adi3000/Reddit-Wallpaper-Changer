using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer.DTOs
{
    public class BalloonTipDetails
    {
        public ToolTipIcon ToolTipIcon { get; }
        public string Title { get; }
        public string Text { get; }
        public int Duration { get; }

        public BalloonTipDetails(ToolTipIcon icon, string title, string text, int duration)
        {
            ToolTipIcon = icon;
            Title = title;
            Text = text;
            Duration = duration;
        }
    }
}
