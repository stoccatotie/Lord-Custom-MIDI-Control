using Melanchall.DryWetMidi.Multimedia;

namespace MidiControl.Core.Services;

public sealed class MidiDeviceService
{
    public IReadOnlyList<string> GetInputDeviceNames()
    {
        return TryGetInputDeviceNames(out var deviceNames)
            ? deviceNames
            : Array.Empty<string>();
    }

    public IReadOnlyList<string> GetOutputDeviceNames()
    {
        return TryGetOutputDeviceNames(out var deviceNames)
            ? deviceNames
            : Array.Empty<string>();
    }

    public bool TryGetInputDeviceNames(out IReadOnlyList<string> deviceNames)
    {
        try
        {
            var devices = InputDevice.GetAll();

            try
            {
                deviceNames = devices.Select(device => device.Name).ToArray();
                return true;
            }
            finally
            {
                DisposeDevices(devices);
            }
        }
        catch
        {
            deviceNames = Array.Empty<string>();
            return false;
        }
    }

    public bool TryGetOutputDeviceNames(out IReadOnlyList<string> deviceNames)
    {
        try
        {
            var devices = OutputDevice.GetAll();

            try
            {
                deviceNames = devices.Select(device => device.Name).ToArray();
                return true;
            }
            finally
            {
                DisposeDevices(devices);
            }
        }
        catch
        {
            deviceNames = Array.Empty<string>();
            return false;
        }
    }

    private static void DisposeDevices<TDevice>(IEnumerable<TDevice> devices)
        where TDevice : IDisposable
    {
        foreach (var device in devices)
        {
            try
            {
                device.Dispose();
            }
            catch
            {
                // Continue releasing the remaining discovery handles.
            }
        }
    }
}
