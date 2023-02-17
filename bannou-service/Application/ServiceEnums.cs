using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Application
{
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
