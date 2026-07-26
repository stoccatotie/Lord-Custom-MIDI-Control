using Melanchall.DryWetMidi.Multimedia;

namespace MidiControl.Core.Services;

public sealed class MidiDeviceService
{
    public IReadOnlyList<string> GetInputDeviceNames()
    {
        try
        {
            var devices = InputDevice.GetAll();

            try
            {
                return devices.Select(device => device.Name).ToArray();
            }
            finally
            {
                foreach (var device in devices)
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> GetOutputDeviceNames()
    {
        try
        {
            var devices = OutputDevice.GetAll();

            try
            {
                return devices.Select(device => device.Name).ToArray();
            }
            finally
            {
                foreach (var device in devices)
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}
