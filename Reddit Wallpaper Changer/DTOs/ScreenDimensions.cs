namespace Reddit_Wallpaper_Changer.DTOs
{
    public class ScreenDimensions
    {
        public int Width { get; }
        public int Height { get; }

        public ScreenDimensions(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }
}