using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;

[assembly: ApiController]
[assembly: InternalsVisibleTo("bannou-service.tests")]
[assembly: InternalsVisibleTo("lib-testing")]
[assembly: InternalsVisibleTo("test-utilities")]
[assembly: InternalsVisibleTo("lib-behavior.tests")]
