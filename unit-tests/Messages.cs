using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Messages : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public class Request_HeaderProperties_Derived : ServiceRequest { }
    public class Request_HeaderProperties_Generic : ServiceRequest<Response_HeaderProperties> { }
    [JsonObject]
    public class Request_JSON_RequiredProperty : ServiceRequest<Response_JSON_RequiredProperty>
    {
        [JsonRequired]
        [JsonProperty("request_id", Required = Required.Always)]
        public string? RequestID { get; set; }
        public Request_JSON_RequiredProperty() { }
    }
    public class Request_HeaderProperties_Generic_Derived : Request_HeaderProperties_Generic { }
    public class Request_HeaderProperties_Generic_Derived_Response : ServiceRequest<Response_HeaderProperties_Derived> { }
    public class Request_HeaderProperties_Additional : ServiceRequest<Response_HeaderProperties_Additional>
    {
        [HeaderArray(Name = "TEST_HEADERS")]
        public Dictionary<string, string>? MoreRequestIDs { get; set; }
    }
    public class Response_HeaderProperties : ServiceResponse { }
    public class Response_HeaderProperties_Derived : Response_HeaderProperties { }
    public class Response_HeaderProperties_Additional : ServiceResponse
    {
        [HeaderArray(Name = "TEST_HEADERS")]
        public Dictionary<string, string>? MoreRequestIDs { get; set; }
    }
    [JsonObject]
    public class Response_JSON_RequiredProperty : ServiceResponse
    {
        [JsonRequired]
        [JsonProperty("request_id", Required = Required.Always)]
        public string? RequestID { get; set; }
        public Response_JSON_RequiredProperty() { }
    }

    public Messages(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Controllers>();
    }

    [Fact]
    public void Messages_TransferHeaders()
    {
        var testID = Guid.NewGuid().ToString();
        var requestModel = new Request_HeaderProperties_Generic
        {
            RequestIDs = new Dictionary<string, string>()
            {
                ["TEST_ID"] = testID
            }
        };

        var responseModel = requestModel.CreateResponse();
        Assert.NotNull(responseModel.RequestIDs);
        Assert.Equal(testID, responseModel.RequestIDs["TEST_ID"]);

        Assert.NotNull(requestModel.RequestIDs);
        Assert.Equal(testID, requestModel.RequestIDs["TEST_ID"]);
    }

    [Fact]
    public void Messages_TransferHeaders_DerivedRequest()
    {
        var testID = Guid.NewGuid().ToString();
        var requestModel = new Request_HeaderProperties_Generic_Derived
        {
            RequestIDs = new Dictionary<string, string>()
            {
                ["TEST_ID"] = testID
            }
        };

        var responseModel = requestModel.CreateResponse();
        Assert.NotNull(responseModel.RequestIDs);
        Assert.Equal(testID, responseModel.RequestIDs["TEST_ID"]);

        Assert.NotNull(requestModel.RequestIDs);
        Assert.Equal(testID, requestModel.RequestIDs["TEST_ID"]);
    }

    [Fact]
    public void Messages_TransferHeaders_DerivedResponse()
    {
        var testID = Guid.NewGuid().ToString();
        var requestModel = new Request_HeaderProperties_Generic_Derived_Response
        {
            RequestIDs = new Dictionary<string, string>()
            {
                ["TEST_ID"] = testID
            }
        };

        var responseModel = requestModel.CreateResponse();
        Assert.NotNull(responseModel.RequestIDs);
        Assert.Equal(testID, responseModel.RequestIDs["TEST_ID"]);

        Assert.NotNull(requestModel.RequestIDs);
        Assert.Equal(testID, requestModel.RequestIDs["TEST_ID"]);
    }

    [Fact]
    public void Messages_TransferHeaders_JsonRequiredProperty()
    {
        var testID = Guid.NewGuid().ToString();
        var requestModel = new Request_JSON_RequiredProperty
        {
            RequestIDs = new Dictionary<string, string>()
            {
                ["TEST_ID"] = testID
            }
        };

        var responseModel = requestModel.CreateResponse();
        Assert.NotNull(responseModel.RequestIDs);
        Assert.Equal(testID, responseModel.RequestIDs["TEST_ID"]);

        Assert.NotNull(requestModel.RequestIDs);
        Assert.Equal(testID, requestModel.RequestIDs["TEST_ID"]);
    }

    [Fact]
    public void Messages_TransferAdditionalHeaders()
    {
        var testID = Guid.NewGuid().ToString();
        var requestModel = new Request_HeaderProperties_Additional
        {
            MoreRequestIDs = new Dictionary<string, string>()
            {
                ["TEST_ID"] = testID
            }
        };

        var responseModel = requestModel.CreateResponse();
        Assert.NotNull(responseModel.MoreRequestIDs);
        Assert.Equal(testID, responseModel.MoreRequestIDs["TEST_ID"]);

        Assert.NotNull(requestModel.MoreRequestIDs);
        Assert.Equal(testID, requestModel.MoreRequestIDs["TEST_ID"]);
    }
}
