using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.Mediator;

#pragma warning disable MA0048 // File name must match type name
public record SwitchToIntroUiMessage : IMessage;
public record SwitchToMainUiMessage : IMessage;
public record OpenSettingsUiMessage : IMessage;
public record DalamudLoginMessage : IMessage;
public record DalamudLogoutMessage : IMessage;
public record FrameworkUpdateMessage : IMessage;
public record ClassJobChangedMessage : IMessage;
public record DelayedFrameworkUpdateMessage : IMessage;
public record ZoneSwitchStartMessage : IMessage;
public record ZoneSwitchEndMessage : IMessage;
public record GposeStartMessage : IMessage;
public record GposeEndMessage : IMessage;
public record GposeFrameworkUpdateMessage : IMessage;
#pragma warning restore MA0048 // File name must match type name