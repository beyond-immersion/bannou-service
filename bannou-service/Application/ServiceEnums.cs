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
}
