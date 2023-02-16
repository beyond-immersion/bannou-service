using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class InventoryService : IDaprService
    {
        /// <summary>
        /// Unique service id for this instance.
        /// </summary>
        public string ServiceID { get; } = $"INVENTORY_{Program.ServiceGUID}";

        void IDaprService.AddEndpointsToWebApp(WebApplication? webApp)
        {
            if (webApp == null)
                return;

            webApp.MapGet("/inventory/create", CreateInventory);
            webApp.MapGet("/inventory/add", AddItems);
            webApp.MapGet("/inventory/remove", RemoveItems);
            webApp.MapGet("/inventory/update", UpdateItem);
            webApp.MapGet("/inventory/transfer", TransferItems);
            webApp.MapGet("/inventory/destroy", DestroyInventory);
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task CreateInventory(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task AddItems(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task RemoveItems(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task UpdateItem(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task TransferItems(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        private async Task DestroyInventory(HttpContext requestContext)
        {
            var response = requestContext.Response;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Plain;
            response.StatusCode = 200;
            await requestContext.Response.StartAsync();
        }
    }
}
