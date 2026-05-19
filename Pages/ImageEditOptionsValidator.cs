using System.Collections.Generic;

namespace Files_Tools.Pages
{
    /// <summary>
    /// Performs lightweight UX-side validation before image processing runs.
    /// </summary>
    public static class ImageEditOptionsValidator
    {
        public static IReadOnlyList<string> Validate(
            ImageEditOptions options,
            int? originalWidth,
            int? originalHeight,
            int? workingWidth = null,
            int? workingHeight = null)
        {
            var errors = new List<string>();
            workingWidth ??= originalWidth;
            workingHeight ??= originalHeight;

            if (!options.PreserveOriginalQuality && (options.QualityPercent < 1 || options.QualityPercent > 100))
            {
                errors.Add("Quality must be between 1% and 100%.");
            }

            if (options.EnableResize && (options.ResizeWidth < 1 || options.ResizeHeight < 1))
            {
                errors.Add("Resize width and height must be greater than 0.");
            }

            if (options.EnableCrop)
            {
                if (options.CropLeft < 0 || options.CropTop < 0 || options.CropWidth < 1 || options.CropHeight < 1)
                {
                    errors.Add("Crop must use a valid image area.");
                }

                if (originalWidth.HasValue && options.CropLeft + options.CropWidth > originalWidth.Value)
                {
                    errors.Add("Crop area cannot exceed the original image width.");
                }

                if (originalHeight.HasValue && options.CropTop + options.CropHeight > originalHeight.Value)
                {
                    errors.Add("Crop area cannot exceed the original image height.");
                }
            }

            if (options.EnableUpscale)
            {
                if (options.UpscaleWidth < 1 || options.UpscaleHeight < 1)
                {
                    errors.Add("Upscale width and height must be greater than 0.");
                }

                if (workingWidth.HasValue && options.UpscaleWidth < workingWidth.Value)
                {
                    errors.Add("Upscale width cannot be smaller than the working image width.");
                }

                if (workingHeight.HasValue && options.UpscaleHeight < workingHeight.Value)
                {
                    errors.Add("Upscale height cannot be smaller than the working image height.");
                }

                if (options.EnableResize && workingWidth.HasValue && workingHeight.HasValue)
                {
                    var resizeWouldReduce =
                        options.ResizeWidth < workingWidth.Value ||
                        options.ResizeHeight < workingHeight.Value;

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
                if (options.RedPercent < 0 || options.RedPercent > 100 ||
                    options.GreenPercent < 0 || options.GreenPercent > 100 ||
                    options.BluePercent < 0 || options.BluePercent > 100)
                {
                    errors.Add("RGB sliders must be between 0% and 100%.");
                }
            }

            return errors;
        }
    }
}
