using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeyondImmersion.ServiceTester.Tests
{
    public interface IServiceTestHandler
    {
        public ServiceTest[] GetServiceTests();
    }
}
