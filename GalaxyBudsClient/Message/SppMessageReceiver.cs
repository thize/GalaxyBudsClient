using System;
using Avalonia.Threading;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model;
using GalaxyBudsClient.Model.Config;
using GalaxyBudsClient.Model.Constants;
using GalaxyBudsClient.Platform;
using Serilog;

namespace GalaxyBudsClient.Message;

public class SppMessageReceiver
{
    private static SppMessageReceiver? _instance;
    private static readonly object SingletonPadlock = new();

    public static SppMessageReceiver Instance
    {
        get
        {
            lock (SingletonPadlock)
            {
                return _instance ??= new SppMessageReceiver();
            }
        }
    }

    public event EventHandler<BaseMessageDecoder>? AnyMessageDecoded;
    public event EventHandler<AcknowledgementDecoder>? AcknowledgementResponse;
    public event EventHandler<MeteringReportDecoder>? MeteringReportResponse;
    public event EventHandler<UsageReportDecoder>? UsageReportResponse;
    public event EventHandler<Usage2ReportDecoder>? Usage2ReportResponse;
    public event EventHandler<GetFmmConfigDecoder>? GetFmmConfigResponse;
    public event EventHandler<byte>? SetFmmConfigResponse;
    public event EventHandler<int>? ResetResponse;
    public event EventHandler<BatteryTypeDecoder>? BatteryTypeResponse;
    public event EventHandler<bool>? AmbientEnabledUpdateResponse;
    public event EventHandler<bool>? AncEnabledUpdateResponse;
    public event EventHandler<NoiseControlModes>? NoiseControlUpdateResponse;
    public event EventHandler<string>? BuildStringResponse;
    public event EventHandler<DebugModeVersionDecoder>? VersionInfoResponse;
    public event EventHandler<DebugGetAllDataDecoder>? GetAllDataResponse;
    public event EventHandler<DebugSerialNumberDecoder>? SerialNumberResponse;
    public event EventHandler<CradleSerialNumberDecoder>? CradleSerialNumberResponse;
    public event EventHandler<SelfTestDecoder>? SelfTestResponse;
    public event EventHandler<TouchOptions>? OtherOption;
    public event EventHandler<ExtendedStatusUpdateDecoder>? ExtendedStatusUpdate;
    public event EventHandler<IBasicStatusUpdate>? BaseUpdate;
    public event EventHandler<StatusUpdateDecoder>? StatusUpdate;
    public event EventHandler<MuteUpdateDecoder>? FindMyGearMuteUpdate;
    public event EventHandler? FindMyGearStopped;
    public event EventHandler<FitTestDecoder>? FitTestResult;
    public event EventHandler<DebugSkuDecoder>? DebugSkuUpdate;
    public event EventHandler<HiddenCmdDataDecoder>? HiddenCmdData;

    public void MessageReceiver(object? sender, SppMessage e)
    {
        var decoder = e.CreateDecoder(); 
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DispatchEventByMessage(e);
            if(decoder != null)
                DispatchEventByDecoder(decoder);
        });
    }

    private void DispatchEventByMessage(SppMessage msg)
    {
        switch (msg.Id)
        {
            case MsgIds.FIND_MY_EARBUDS_STOP:
                FindMyGearStopped?.Invoke(this, EventArgs.Empty);
                break;
            case MsgIds.TOUCH_ON_BUDS:
                HandleTouchOnBuds(msg);
                break;
        }
    }

    /* Last known touch options of each earbud (from status updates or from the app changing them) */
    public TouchOptions? TouchOptionLeft { set; get; }
    public TouchOptions? TouchOptionRight { set; get; }

    private const byte TouchOnBudsHoldStart = 0x04;
    private bool _receivedOtherOptionMessage;

    /// <summary>
    /// Newer models (e.g. Buds3 Pro) don't send SET_TOUCHPAD_OTHER_OPTION for the custom long-press action.
    /// Instead, they report the gesture via TOUCH_ON_BUDS: payload[1] = side (0 = left, 1 = right),
    /// payload[2] = gesture (0x04 = hold started, 0x05 = hold released).
    /// </summary>
    private void HandleTouchOnBuds(SppMessage msg)
    {
        Log.Debug("SppMessageReceiver: TOUCH_ON_BUDS {Payload}", BitConverter.ToString(msg.Payload));

        if (_receivedOtherOptionMessage || msg.Payload.Length < 3 || msg.Payload[2] != TouchOnBudsHoldStart)
            return;

        var isLeft = msg.Payload[1] == 0;
        var option = isLeft ? TouchOptions.OtherL : TouchOptions.OtherR;
        var configured = isLeft ? TouchOptionLeft : TouchOptionRight;
        if (configured != option)
            return;

        OtherOption?.Invoke(this, option);
    }

    private void DispatchEventByDecoder(BaseMessageDecoder decoder)
    {
        AnyMessageDecoded?.Invoke(this, decoder);
        
        switch (decoder)
        {
            case AcknowledgementDecoder p:
                AcknowledgementResponse?.Invoke(this, p);
                break;
            case GetFmmConfigDecoder p:
                GetFmmConfigResponse?.Invoke(this, p);
                break;
            case SetFmmConfigDecoder p:
                SetFmmConfigResponse?.Invoke(this, p.Response);
                break;
            case UsageReportDecoder p:
                UsageReportResponse?.Invoke(this, p);
                break;
            case Usage2ReportDecoder p:
                Usage2ReportResponse?.Invoke(this, p);
                break;
            case MeteringReportDecoder p:
                MeteringReportResponse?.Invoke(this, p);
                break;
            case ResetResponseDecoder p:
                ResetResponse?.Invoke(this, p.ResultCode);
                break;
            case BatteryTypeDecoder p:
                BatteryTypeResponse?.Invoke(this, p);
                break;
            case AmbientModeUpdateDecoder p:
                AmbientEnabledUpdateResponse?.Invoke(this, p.Enabled);
                break;
            case DebugModeVersionDecoder p:
                VersionInfoResponse?.Invoke(this, p);
                break;
            case DebugBuildInfoDecoder p:
                BuildStringResponse?.Invoke(this, p.BuildString ?? "null");
                break;
            case DebugGetAllDataDecoder p:
                GetAllDataResponse?.Invoke(this, p);
                break;
            case DebugSerialNumberDecoder p:
                SerialNumberResponse?.Invoke(this, p);
                break;
            case CradleSerialNumberDecoder p:
                CradleSerialNumberResponse?.Invoke(this, p);
                break;
            case ExtendedStatusUpdateDecoder p:
                var device = BluetoothImpl.Instance.Device.Current;
                if (device != null) 
                    device.DeviceColor = p.DeviceColor;

                TouchOptionLeft = p.TouchpadOptionL;
                TouchOptionRight = p.TouchpadOptionR;
                BaseUpdate?.Invoke(this, p);
                ExtendedStatusUpdate?.Invoke(this, p);
                break;
            case FitTestDecoder p:
                FitTestResult?.Invoke(this, p);
                break;
            case SelfTestDecoder p:
                SelfTestResponse?.Invoke(this, p);
                break;
            case SetOtherOptionDecoder p:
                _receivedOtherOptionMessage = true;
                OtherOption?.Invoke(this, p.OptionType);
                break;
            case StatusUpdateDecoder p:
                StatusUpdate?.Invoke(this, p);
                BaseUpdate?.Invoke(this, p);
                break;
            case MuteUpdateDecoder p:
                FindMyGearMuteUpdate?.Invoke(this, p);
                break;
            case NoiseReductionModeUpdateDecoder p:
                AncEnabledUpdateResponse?.Invoke(this, p.Enabled);
                break;
            case NoiseControlUpdateDecoder p:
                NoiseControlUpdateResponse?.Invoke(this, p.Mode);
                break;
            case DebugSkuDecoder p:
                DebugSkuUpdate?.Invoke(this, p);
                break; 
            case HiddenCmdDataDecoder p:
                HiddenCmdData?.Invoke(this, p);
                break;
            case VoiceWakeupEventDecoder p:
                if (p.ResultCode == 1)
                {
                    Log.Debug("SppMessageHandler: Voice wakeup event received");
                    EventDispatcher.Instance.Dispatch(Settings.Data.BixbyRemapEvent);
                }
                break;
        }
    }

}