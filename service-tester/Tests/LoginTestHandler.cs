using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeyondImmersion.ServiceTester.Tests
{
    public class LoginTestHandler : IServiceTestHandler
    {
        public ServiceTest[] GetServiceTests()
        {
            return new ServiceTest[]
            {
                new ServiceTest(LoginTest_NoQueue, "Login - No Queue", "HTTP",
                    "Attempt to connect to the login endpoint, expecting a 200 response containing the url of the client handler to connect to next.")
            };
        }

        public void LoginTest_NoQueue(string[] args)
        {
            Console.WriteLine("!!!");
        }
    }
}
