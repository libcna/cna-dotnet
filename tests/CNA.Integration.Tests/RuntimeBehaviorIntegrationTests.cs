using CNA.Audio;
using Xunit;

namespace CNA.Integration.Tests;

/// <summary>Focused regressions for framework work that only becomes observable at a native frame
/// boundary. These deliberately assert exact callback counts: merely seeing at least one callback
/// would not detect the managed/native double-pump regression.</summary>
[Collection(NativeGameCollection.Name)]
public sealed class RuntimeBehaviorIntegrationTests(NativeGameFixture fixture)
{
    /// <summary>A Play notification is delivered once, including while paused.</summary>
    [NativeFact]
    public void GameUpdate_DeliversPostedBufferNeededOnce()
    {
        DynamicSoundEffectInstance? dynamic = null;
        int events = 0;
        var pendingAtEachRaise = new List<int>();

        fixture.InsideAFrame(_ =>
        {
            dynamic = new DynamicSoundEffectInstance(8000, AudioChannels.Mono);
            dynamic.SubmitBuffer(new byte[16_000]);
            dynamic.BufferNeeded += (_, _) =>
            {
                events++;
                pendingAtEachRaise.Add(dynamic!.PendingBufferCount);
            };
            dynamic.Play();
            dynamic.Pause();
        });

        Assert.True(fixture.LastFrameUpdateCount >= 1, "The frame ran no update at all.");
        Assert.All(pendingAtEachRaise, pending => Assert.Equal(1, pending));
        Assert.Equal(1, events);
        fixture.InsideAFrame(_ => { });
        Assert.Equal(1, events);
        fixture.InsideAFrame(_ =>
        {
            dynamic!.Stop();
            dynamic.Dispose();
        });
    }

    [NativeFact]
    public void DynamicSoundHandlerException_DoesNotUnwindThroughNativeFrame()
    {
        DynamicSoundEffectInstance? dynamic = null;

        fixture.InsideAFrame(_ =>
        {
            dynamic = new DynamicSoundEffectInstance(8000, AudioChannels.Mono);
            dynamic.SubmitBuffer(new byte[16_000]);
            dynamic.BufferNeeded += (_, _) => throw new InvalidOperationException("handler-failure");
            dynamic.Play();
        });

        fixture.InsideAFrame(_ =>
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(dynamic!.Dispose);
            Assert.Equal("handler-failure", exception.Message);
            Assert.True(exception.Data.Contains("CnaNativeEvent"));
        });
    }

    [NativeFact]
    public void GraphicsDeviceReset_RaisesResettingThenResetWithCorrectSender()
    {
        fixture.InsideAFrameWithDevice(device =>
        {
            var order = new List<string>();
            bool sendersMatch = true;
            EventHandler<EventArgs> resetting = (sender, _) =>
            {
                sendersMatch &= ReferenceEquals(sender, device);
                order.Add("resetting");
            };
            EventHandler<EventArgs> reset = (sender, _) =>
            {
                sendersMatch &= ReferenceEquals(sender, device);
                order.Add("reset");
            };

            device.DeviceResetting += resetting;
            device.DeviceReset += reset;
            try
            {
                device.Reset();
            }
            finally
            {
                device.DeviceResetting -= resetting;
                device.DeviceReset -= reset;
            }

            Assert.True(sendersMatch);
            Assert.Equal(new[] { "resetting", "reset" }, order);
        });
    }
}
