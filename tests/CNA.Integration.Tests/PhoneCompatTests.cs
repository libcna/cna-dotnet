using CNA.XnaCompat.Extensions;
using Microsoft.Devices;
using Microsoft.Devices.Sensors;
using Xunit;
using Xunit.Abstractions;
using XnaGame = Microsoft.Xna.Framework.Game;
using XnaGameTime = Microsoft.Xna.Framework.GameTime;
using XnaGraphicsDeviceManager = Microsoft.Xna.Framework.GraphicsDeviceManager;

namespace CNA.Integration.Tests;

/// <summary>
/// The opt-in phone assembly over CNA's devices module, inside a running XNA-style game, which is
/// what every device route is addressed through.
/// </summary>
[Collection(OwnGameCollection.Name)]
public class PhoneCompatTests(ITestOutputHelper output)
{
    [NativeFact]
    public void Environment_AndVibration_AnswerLikeAPhone()
    {
        using var game = new Host();
        game.Inside(() =>
        {
            // A desktop host is the emulator's stand-in, as in CNA's C++ (only a mobile platform
            // answers Device).
            Assert.Equal(DeviceType.Emulator, Microsoft.Devices.Environment.DeviceType);
            Assert.Same(VibrateController.Default, VibrateController.Default);
            VibrateController.Default.Start(TimeSpan.FromMilliseconds(50));
            VibrateController.Default.Stop();
            VibrateController.Default.Stop();
            Assert.Equal("duration", Assert.Throws<ArgumentOutOfRangeException>(
                () => VibrateController.Default.Start(TimeSpan.FromSeconds(6))).ParamName);
            Assert.Throws<ArgumentOutOfRangeException>(() => VibrateController.Default.Start(TimeSpan.FromTicks(-1)));
        });
    }

    /// <summary>
    /// A desktop without the sensor: unsupported, and Start refuses with the exception phone games
    /// catch to fall back to other input (Bounce, Platformer and MarbleMaze all do).
    /// </summary>
    [NativeFact]
    public void Accelerometer_WithoutHardware_RefusesToStartAsAPhoneWould()
    {
        using var game = new Host();
        game.Inside(() =>
        {
            output.WriteLine($"IsSupported={Accelerometer.IsSupported}");
            using var sensor = new Accelerometer();
            if (Accelerometer.IsSupported)
            {
                return;
            }

            Assert.Equal(SensorState.NotSupported, sensor.State);
            AccelerometerFailedException failure = Assert.Throws<AccelerometerFailedException>(sensor.Start);
            output.WriteLine($"ErrorId={failure.ErrorId} {failure.Message}");
            Assert.IsAssignableFrom<SensorFailedException>(failure);
        });
    }

    /// <summary>
    /// Readings flow through both events in native's order -- the current-value event first, the
    /// 7.0 ReadingChanged second -- converted to g, and the current value follows.
    /// </summary>
    [NativeFact]
    public void Accelerometer_DeliversReadingsThroughBothEvents()
    {
        using var game = new Host();
        game.Inside(() =>
        {
            var sensor = new Accelerometer();
            sensor.SetSupportedForTests(true);
            var order = new List<string>();
            AccelerometerReading current = default;
            AccelerometerReadingEventArgs? legacy = null;
            sensor.CurrentValueChanged += (sender, e) =>
            {
                Assert.Same(sensor, sender);
                order.Add("current");
                current = e.SensorReading;
            };
#pragma warning disable CS0618
            sensor.ReadingChanged += (sender, e) =>
            {
                order.Add("legacy");
                legacy = e;
            };
#pragma warning restore CS0618

            // Injection is native's stand-in for the sensor thread and reaches only a started
            // sensor; Start would try to open a real sensor here and refuse, so the started state is
            // set the way native's own tests set it.
            sensor.SetStartedForTests(true);
            sensor.InjectForTests(0f, 9.80665f, 4.903325f);
            Assert.Equal(["current", "legacy"], order);
            Assert.Equal(1f, current.Acceleration.Y, 4);
            Assert.Equal(0.5f, current.Acceleration.Z, 4);
            Assert.NotNull(legacy);
            Assert.Equal(1.0, legacy!.Y, 4);
            Assert.True(sensor.IsDataValid);
            Assert.Equal(1f, sensor.CurrentValue.Acceleration.Y, 4);

            sensor.TimeBetweenUpdates = TimeSpan.FromMilliseconds(20);
            Assert.Equal(TimeSpan.FromMilliseconds(20), sensor.TimeBetweenUpdates);

            sensor.SetStartedForTests(false);
            sensor.Dispose();
            Assert.Throws<ObjectDisposedException>(sensor.Dispose);
            Assert.Throws<ObjectDisposedException>(() => sensor.State);
        });
    }

    private sealed class Host : XnaGame
    {
        private Action? _body;
        private Exception? _failure;

        public Host()
        {
            _ = new XnaGraphicsDeviceManager(this);
        }

        public void Inside(Action body)
        {
            _body = body;
            this.RunFrameEXT();
            if (_failure is { } failure)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        protected override void Update(XnaGameTime gameTime)
        {
            if (_body is { } body)
            {
                _body = null;
                try
                {
                    body();
                }
                catch (Exception exception)
                {
                    _failure = exception;
                }
            }

            base.Update(gameTime);
        }
    }
}
