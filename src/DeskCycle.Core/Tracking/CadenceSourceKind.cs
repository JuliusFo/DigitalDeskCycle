using System.Text.Json.Serialization;

namespace DeskCycle.Core.Tracking;

/// <summary>
/// The kind of link a source uses. Separate from
/// <see cref="LiveStatus.SourceName"/>, which stays free-form ("COM3", the
/// device name): the display picks its icon by kind, the name only fills the
/// tooltip.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CadenceSourceKind>))]
public enum CadenceSourceKind
{
    /// <summary>Nothing is delivering samples.</summary>
    None = 0,

    Usb,

    Bluetooth,
}
