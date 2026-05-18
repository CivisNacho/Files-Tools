using System.Collections.Generic;

namespace Files_Tools.ImageEditing
{
    /// <summary>
    /// Performs lightweight UX-side validation before image processing runs.
    /// </summary>
    public static class ImageEditOptionsValidator
    {
        public static IReadOnlyList<string> Validate(ImageEditOptions options, int? originalWidth, int? originalHeight)
        {
            var errors = new List<string>();

            if (!options.PreserveOriginalQuality && (options.QualityPercent < 1 || options.QualityPercent > 100))
            {
                errors.Add("Quality must be between 1% and 100%.");
            }

            if (options.EnableResize && (options.ResizeWidth < 1 || options.ResizeHeight < 1))
            {
                errors.Add("Resize width and height must be greater than 0.");
            }

            if (options.EnableUpscale)
            {
                if (options.UpscaleWidth < 1 || options.UpscaleHeight < 1)
                {
                    errors.Add("Upscale width and height must be greater than 0.");
                }

                if (originalWidth.HasValue && options.UpscaleWidth < originalWidth.Value)
                {
                    errors.Add("Upscale width cannot be smaller than the original width.");
                }

                if (originalHeight.HasValue && options.UpscaleHeight < originalHeight.Value)
                {
                    errors.Add("Upscale height cannot be smaller than the original height.");
                }

                if (options.EnableResize && originalWidth.HasValue && originalHeight.HasValue)
                {
                    var resizeWouldReduce =
                        options.ResizeWidth < originalWidth.Value ||
                        options.ResizeHeight < originalHeight.Value;

                    if (resizeWouldReduce)
                    {
                        errors.Add("Upscale cannot be combined with a resize that reduces the image size.");
                    }
                }
            }

            if (options.EnableRotation &&
                options.RotationDegrees is not 0 and not 90 and not 180 and not 270)
            {
                errors.Add("Rotation only supports 90 degree jumps.");
            }

            if (options.EnableRgbAdjustments)
            {
                if (options.RedPercent < 0 || options.RedPercent > 200 ||
                    options.GreenPercent < 0 || options.GreenPercent > 200 ||
                    options.BluePercent < 0 || options.BluePercent > 200)
                {
                    errors.Add("RGB sliders must be between 0% and 200%.");
                }
            }

            return errors;
        }
    }
}
