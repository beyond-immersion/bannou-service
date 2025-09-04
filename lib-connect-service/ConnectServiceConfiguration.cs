using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Connect;

[ServiceConfiguration(typeof(ConnectService))]
public class ConnectServiceConfiguration : BaseServiceConfiguration
{
    public bool Obfuscate_Not_Found_Response { get; set; } = true;
    public uint Client_Request_Max_Size { get; set; } = 16 * 1024;
    public uint Client_Response_Max_Size { get; set; } = 16 * 1024;
    public uint Client_Reconnection_Time { get; set; } = 30;

    [ConfigRequired]
    public string? Redis_Connection_String { get; set; }

    [ConfigRequired]
    public string? Token_Public_Key { get; set; }
}
