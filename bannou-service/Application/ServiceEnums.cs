using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Application
{
    /// <summary>
    /// Enumeration for common/supported HTTP methods.
    /// 
    /// (Not sure why there isn't an enum for this in .NET)
    /// </summary>
    public enum HttpMethodTypes
    {
        GET = 0,
        POST,
        PUT,
        DELETE,

        // unsupported yet
        HEAD,
        PATCH,
        OPTIONS
    }

    /// <summary>
    /// An enumeration of possible API response codes
    /// returned by this service.
    /// </summary>
    public enum ResponseCodes
    {
        Ok = 0,
        Accepted,
        BadRequest,
        Unauthorized,
        ServerBusy,
        ServerError
    }
}
