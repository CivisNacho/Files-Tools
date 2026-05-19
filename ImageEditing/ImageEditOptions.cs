namespace Files_Tools.ImageEditing
{
    /// <summary>
    /// Represents all user-selected operations for a single image edit run.
    /// Multiple options can be combined in one execution.
    /// </summary>
    public sealed class ImageEditOptions
    {
        public ImageFileFormat OutputFormat { get; set; } = ImageFileFormat.KeepOriginal;

        public bool PreserveOriginalQuality { get; set; } = true;

        public int QualityPercent { get; set; } = 90;

        public bool EnableCrop { get; set; }

        public int CropLeft { get; set; }

        public int CropTop { get; set; }

        public int CropWidth { get; set; }

        public int CropHeight { get; set; }

        public bool EnableResize { get; set; }

        public int ResizeWidth { get; set; } = 1920;

        public int ResizeHeight { get; set; } = 1080;

        public bool PreserveAspectRatio { get; set; } = true;

        public bool EnableUpscale { get; set; }

        public int UpscaleWidth { get; set; } = 2560;

        public int UpscaleHeight { get; set; } = 1440;

        public bool EnableRotation { get; set; }

        public int RotationDegrees { get; set; }

        public bool MirrorHorizontally { get; set; }

        public bool MirrorVertically { get; set; }

        public bool EnableRgbAdjustments { get; set; }

        public int RedPercent { get; set; } = 100;

        public int GreenPercent { get; set; } = 100;

        public int BluePercent { get; set; } = 100;
    }
}
