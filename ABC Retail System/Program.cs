using ABC_Retail_System.Services;
using ABC_Retail_System.Services.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using Azure.Storage.Queues;

namespace ABC_Retail_System
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder MapStaticAssets(this IApplicationBuilder app)
        {
            app.UseStaticFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")),
                RequestPath = "/wwwroot"
            });
            return app;
        }

        public static IEndpointConventionBuilder WithStaticAssets(this IEndpointConventionBuilder builder)
        {
            return builder;
        }
    }

    public class Program
    {
        private static string GetStorageAccountName(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return "[No connection string]";

            try
            {
                var accountName = connectionString
                    .Split(';')
                    .FirstOrDefault(part => part.Trim().StartsWith("AccountName="))
                    ?.Split('=')[1]
                    .Trim();

                return accountName ?? "[Account name not found]";
            }
            catch
            {
                return "[Error parsing account name]";
            }
        }

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            // Retrieve the connection string from the appsettings.json file
            var storageConnectionString = builder.Configuration.GetConnectionString("storageConnectionString")
                ?? throw new InvalidOperationException("Storage connection string is missing");

            // Register storage services
            builder.Services.AddSingleton<TableStorageService>(sp =>
                new TableStorageService(storageConnectionString, "abcretail"));

            builder.Services.AddSingleton<QueueStorageService>(sp =>
                new QueueStorageService(storageConnectionString, "abcretail-queue"));

            builder.Services.AddSingleton(sp =>
                new BlobStorageService(storageConnectionString, "abcretail-blobs"));
                
            // Register QueueServiceClient
            builder.Services.AddSingleton(sp => 
                new QueueServiceClient(storageConnectionString));

            // Register FileShareStorageService
            Console.WriteLine("\n=== Starting File Share Service Initialization ===");
            try
            {
                var accountName = GetStorageAccountName(storageConnectionString);
                Console.WriteLine($"[DEBUG] Storage Account: {accountName}");
                Console.WriteLine($"[DEBUG] Share Name: abcretail-logs");

                // Log the first few characters of the connection string (don't log the full key for security)
                var safeConnectionString = string.IsNullOrEmpty(storageConnectionString)
                    ? "[EMPTY]"
                    : storageConnectionString.Substring(0, Math.Min(30, storageConnectionString.Length)) + "...";
                Console.WriteLine($"[DEBUG] Connection String: {safeConnectionString}");

                try
                {
                    Console.WriteLine("[DEBUG] Creating FileShareStorageService instance...");
                    var fileShareService = new FileShareStorageService(storageConnectionString, "abcretail-logs");

                    Console.WriteLine("[DEBUG] Initializing file share...");
                    await fileShareService.InitializeAsync();
                    Console.WriteLine("[SUCCESS] File share initialized successfully");

                    // Test the file share by listing files
                    try
                    {
                        Console.WriteLine("[DEBUG] Attempting to list files in share...");
                        var files = await fileShareService.ListFilesAsync();
                        Console.WriteLine($"[INFO] Found {files.Count} files in the share");
                    }
                    catch (Exception listEx)
                    {
                        Console.WriteLine($"[WARNING] Could not list files in share: {listEx.Message}");
                        if (listEx.InnerException != null)
                        {
                            Console.WriteLine($"[WARNING] Inner exception: {listEx.InnerException.Message}");
                        }
                    }

                    builder.Services.AddSingleton<FileShareStorageService>(_ => fileShareService);
                    Console.WriteLine("[SUCCESS] File share service registered in DI container");
                }
                catch (Exception serviceEx)
                {
                    Console.WriteLine($"[ERROR] Error in file share service: {serviceEx.Message}");
                    if (serviceEx.InnerException != null)
                    {
                        Console.WriteLine($"[ERROR] Inner exception: {serviceEx.InnerException.Message}");
                        if (serviceEx.InnerException.InnerException != null)
                        {
                            Console.WriteLine($"[ERROR] Inner inner exception: {serviceEx.InnerException.InnerException.Message}");
                        }
                    }
                    Console.WriteLine($"[DEBUG] Stack trace: {serviceEx.StackTrace}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[CRITICAL] ===== FILE SHARE INITIALIZATION FAILED =====");
                Console.WriteLine($"[CRITICAL] Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[CRITICAL] Inner: {ex.InnerException.Message}");
                }
                Console.WriteLine($"[CRITICAL] Stack trace: {ex.StackTrace}");
                Console.WriteLine("[CRITICAL] ===========================================\n");
                throw;
            }
            Console.WriteLine("=== File Share Service Initialization Complete ===\n");

            // Register business services
            builder.Services.AddScoped<OrderService>();
            builder.Services.AddScoped<CustomerService>();
            builder.Services.AddScoped<ProductService>();
            
            // Register QueueLoggerService as a hosted service
            builder.Services.AddSingleton<QueueServiceClient>(sp => 
                new QueueServiceClient(builder.Configuration.GetConnectionString("storageConnectionString")));
                
            builder.Services.AddHostedService<QueueLoggerService>();
            
            // Register QueueLoggerService for dependency injection in controllers
            builder.Services.AddScoped<QueueLoggerService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            app.Run();
        }
    }
}