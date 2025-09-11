namespace BeyondImmersion.ServiceTester.Tests;

public class AuthTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new ServiceTest[]
        {
            new ServiceTest(AuthTest_NoQueue, "Auth - No Queue", "HTTP",
                "Attempt to connect to the auth endpoint, expecting a 400 response.")
        };
    }

    public void AuthTest_NoQueue(string[] args) => Console.WriteLine("!!!");
}
