using Android.Hardware;
using Golether.Core.Data.Enums;
using Golether.Media.Conference;

namespace Golether.Mobile.Android;

/// <summary>
/// Finds the cameras of the phone and works out how to stand their picture upright.
/// </summary>
internal static class PhoneCameras
{
    /// <summary>
    /// Gets a value indicating whether the phone has a camera on each side.
    /// </summary>
    public static bool HasBothSides
    {
        get
        {
            var facings = new HashSet<bool>();

            // The camera element of GStreamer opens the old API, and its numbers are not those of camera2.
#pragma warning disable CS0618, CA1422
            for (var id = 0; id < Camera.NumberOfCameras; id++)
            {
                var info = new Camera.CameraInfo();
                Camera.GetCameraInfo(id, info);
                facings.Add(info.Facing == CameraFacing.Front);
            }
#pragma warning restore CS0618, CA1422

            return facings.Count > 1;
        }
    }

    /// <summary>
    /// Finds a camera; a device without one on the wanted side gives the first one it has.
    /// </summary>
    /// <param name="screenRotation">How many degrees the screen is turned from its natural position.</param>
    /// <param name="front">Whether the camera should look at the user.</param>
    /// <returns>The camera, or <see langword="null"/> when there is none.</returns>
    public static CaptureDevice? Find(int screenRotation, bool front)
    {
#pragma warning disable CS0618, CA1422
        CaptureDevice? found = null;
        for (var id = 0; id < Camera.NumberOfCameras; id++)
        {
            var info = new Camera.CameraInfo();
            Camera.GetCameraInfo(id, info);

            var isFront = info.Facing == CameraFacing.Front;
            if (found is null || isFront == front)
            {
                found = Describe(id, Rotation(info.Orientation, screenRotation, isFront));
            }

            if (isFront == front)
            {
                break;
            }
        }
#pragma warning restore CS0618, CA1422

        return found;
    }

    /// <summary>
    /// Works out the turn of the picture: the sensor is fixed in the body of the phone, and the front one faces the
    /// other way, so the turn of the screen adds up for it and subtracts for the back one.
    /// </summary>
    /// <param name="sensorOrientation">How the sensor is mounted, in degrees clockwise.</param>
    /// <param name="screenRotation">How many degrees the screen is turned from its natural position.</param>
    /// <param name="isFront">Whether the camera looks at the user.</param>
    /// <returns>The degrees clockwise.</returns>
    private static int Rotation(int sensorOrientation, int screenRotation, bool isFront)
        => (isFront ? sensorOrientation + screenRotation : sensorOrientation - screenRotation + 360) % 360;

    /// <summary>
    /// Describes a camera for the conference.
    /// </summary>
    /// <param name="id">The number of the camera.</param>
    /// <param name="rotation">The turn of the picture.</param>
    /// <returns>The device.</returns>
    private static CaptureDevice Describe(int id, int rotation)
        => new(id.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty, CaptureDeviceKind.Camera, rotation);
}
