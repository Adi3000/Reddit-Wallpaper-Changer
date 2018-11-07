namespace Reddit_Wallpaper_Changer
{
    public enum WallpaperGrabType
    {
        Random,
        Newest,
        HotToday,
        TopLastHour,
        TopToday,
        TopWeek,
        TopMonth,
        TopYear,
        TopAllTime,
        TrulyRandom
    }

    //======================================================================
    // Wallpaper layout styles
    //======================================================================
    public enum WallpaperStyle
    {
        Default,
        Stretch = 2,
        Fit = 6,
        Fill = 10,        
        Span = 22
    }

    public enum TileWallpaper
    {
        None,
        Tile
    }

    public enum LogLevel
    {
        Information,
        Warning,
        Error
    }

    public enum ChangeTimeType
    {
        Minutes,
        Hours,
        Days
    }
}
