using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using IISDeploymentAutomation.Services;
using IISDeploymentAutomation.Interfaces;
using IISDeploymentAutomation.Models;
using IISDeploymentAutomation.Utils;
using System.Diagnostics;

namespace IISDeploymentAutomation
{
    /// <summary>
    /// Main entry point for the IIS Deployment Automation System
    /// </summary>
    public class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                Console.WriteLine("=================================================================");
                Console.WriteLine("         IIS Deployment Automation System v1.0                 ");                
                Console.WriteLine("=================================================================");
                Console.WriteLine();

                // Initial permission check
                if (!SecurityUtils.IsRunningAsAdministrator())
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR: This application must be run as Administrator!");
                    Console.WriteLine("Please restart the application with administrator privileges.");
                    Console.ResetColor();
                    Console.WriteLine();
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return 1;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Administrator privileges verified");
                Console.ResetColor();

                // Setup configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddCommandLine(args)
                    .Build();

                // Setup Serilog
                SetupLogging(configuration);

                Log.Information("Starting IIS Deployment Automation System");
                Log.Information("Application started by {User} on {Machine}", 
                    Environment.UserName, Environment.MachineName);

                // Create host
                var host = CreateHostBuilder(args, configuration).Build();

                // Validate configuration and permissions before starting
                await ValidateSystemRequirements(host);

                // Display startup information
                await DisplayStartupInformation(host);

                // Check for command line operations
                if (args.Length > 0)
                {
                    return await HandleCommandLineOperations(host, args);
                }

                // Start the interactive service
                await RunInteractiveMode(host);

                return 0;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Fatal Error: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return 1;
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }

        /// <summary>
        /// Creates the host builder with dependency injection
        /// </summary>
        private static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration)
        {
            return Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .UseWindowsService(options =>
                {
                    options.ServiceName = "IISDeploymentAutomation";
                })
                .ConfigureServices((context, services) =>
                {
                    // Configuration
                    services.AddSingleton(configuration);

                    // Core services
                    services.AddSingleton<IAuditService, AuditService>();
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<IPermissionValidationService, PermissionValidationService>();
                    services.AddSingleton<IIISManagerService, IISManagerService>();
                    services.AddSingleton<IFileSystemMonitorService, FileSystemMonitorService>();

                    // Hosted service for orchestration
                    services.AddHostedService<DeploymentOrchestrationHostedService>();
                });
        }

        /// <summary>
        /// Sets up structured logging with Serilog
        /// </summary>
        private static void SetupLogging(IConfiguration configuration)
        {
            var logLevel = configuration["Logging:LogLevel:Default"] ?? "Information";
            var logPath = configuration["Logging:FilePath"] ?? @".\Logs\deployment-{Date}.log";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(logLevel))
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "IISDeploymentAutomation")
                .Enrich.WithProperty("Version", "1.0.0")
                .Enrich.WithProperty("Environment", Environment.MachineName)
                .WriteTo.Console(outputTemplate: 
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.File(logPath, 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: 
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .CreateLogger();
        }

        /// <summary>
        /// Validates system requirements and permissions
        /// </summary>
        private static async Task ValidateSystemRequirements(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var permissionService = scope.ServiceProvider.GetRequiredService<IPermissionValidationService>();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            Console.WriteLine();
            Console.WriteLine("Validating system requirements...");

            // Validate permissions
            var permissionResult = await permissionService.ValidatePermissionsAsync();
            
            foreach (var check in permissionResult.Checks)
            {
                var status = check.Passed ? "✓" : "✗";
                var color = check.Passed ? ConsoleColor.Green : ConsoleColor.Red;
                
                Console.ForegroundColor = color;
                Console.WriteLine($"  {status} {check.Name}");
                Console.ResetColor();
                
                if (!check.Passed && !string.IsNullOrEmpty(check.ErrorMessage))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    └─ {check.ErrorMessage}");
                    Console.ResetColor();
                }
            }

            if (!permissionResult.IsValid)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("CRITICAL: System requirements validation failed!");
                Console.WriteLine("Please address the permission issues above before continuing.");
                Console.ResetColor();
                throw new InvalidOperationException("System requirements validation failed");
            }

            // Validate configuration
            Console.WriteLine();
            Console.WriteLine("Loading and validating configuration...");
            
            var config = await configService.LoadConfigurationAsync();
            var configValid = await configService.ValidateConfigurationAsync(config);
            
            if (configValid)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Configuration loaded successfully ({config.Applications.Count} applications configured)");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Configuration validation failed");
                Console.ResetColor();
                throw new InvalidOperationException("Configuration validation failed");
            }

            logger.LogInformation("System validation completed successfully");
        }

        /// <summary>
        /// Displays startup information and configuration summary
        /// </summary>
        private static async Task DisplayStartupInformation(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var iisService = scope.ServiceProvider.GetRequiredService<IIISManagerService>();

            var config = await configService.LoadConfigurationAsync();
            var appPools = await iisService.GetApplicationPoolsAsync();
            var sites = await iisService.GetSitesAsync();

            Console.WriteLine();
            Console.WriteLine("System Information:");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
            Console.WriteLine($"  Machine Name:           {Environment.MachineName}");
            Console.WriteLine($"  User Context:           {Environment.UserDomainName}\\{Environment.UserName}");
            Console.WriteLine($"  Working Directory:      {AppDomain.CurrentDomain.BaseDirectory}");
            Console.WriteLine($"  Configuration File:     {configService.DefaultConfigPath}");
            Console.WriteLine($"  Available App Pools:    {appPools.Count}");
            Console.WriteLine($"  Available Sites:        {sites.Count}");
            Console.WriteLine($"  Configured Applications: {config.Applications.Count}");
            Console.WriteLine();

            if (config.Applications.Any())
            {
                Console.WriteLine("Configured Applications:");
                Console.WriteLine("─────────────────────────────────────────────────────────────────");
                foreach (var app in config.Applications.Where(a => a.IsEnabled))
                {
                    var poolExists = appPools.Contains(app.ApplicationPoolName);
                    var siteExists = sites.Contains(app.SiteName);
                    
                    Console.WriteLine($"  {app.Name}");
                    Console.WriteLine($"    └─ App Pool:     {app.ApplicationPoolName} {(poolExists ? "✓" : "✗")}");
                    Console.WriteLine($"    └─ Site:         {app.SiteName} {(siteExists ? "✓" : "✗")}");
                    Console.WriteLine($"    └─ Source:       {app.SourcePath}");
                    Console.WriteLine($"    └─ Destination:  {app.DestinationPath}");
                    Console.WriteLine($"    └─ Watch Folders: {app.WatchFolders.Count}");
                    Console.WriteLine();
                }
            }
        }

        /// <summary>
        /// Handles command line operations
        /// </summary>
        private static async Task<int> HandleCommandLineOperations(IHost host, string[] args)
        {
            // Parse command line arguments
            var command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "--validate":
                case "-v":
                    Console.WriteLine("System validation completed successfully.");
                    return 0;

                case "--list-pools":
                case "-lp":
                    await ListApplicationPools(host);
                    return 0;

                case "--list-sites":
                case "-ls":
                    await ListSites(host);
                    return 0;

                case "--deploy":
                case "-d":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: --deploy <application-name>");
                        return 1;
                    }
                    return await TriggerDeployment(host, args[1]);

                case "--help":
                case "-h":
                case "/?":
                    DisplayHelp();
                    return 0;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    Console.WriteLine("Use --help for available commands.");
                    return 1;
            }
        }

        /// <summary>
        /// Lists all available application pools
        /// </summary>
        private static async Task ListApplicationPools(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var iisService = scope.ServiceProvider.GetRequiredService<IIISManagerService>();

            var pools = await iisService.GetApplicationPoolsAsync();
            
            Console.WriteLine();
            Console.WriteLine("Available Application Pools:");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
            
            foreach (var pool in pools.OrderBy(p => p))
            {
                var state = await iisService.GetApplicationPoolStateAsync(pool);
                var stateColor = state switch
                {
                    AppPoolState.Started => ConsoleColor.Green,
                    AppPoolState.Stopped => ConsoleColor.Red,
                    AppPoolState.Starting => ConsoleColor.Yellow,
                    AppPoolState.Stopping => ConsoleColor.Yellow,
                    _ => ConsoleColor.Gray
                };

                Console.ForegroundColor = stateColor;
                Console.WriteLine($"  {pool} ({state})");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Lists all available sites
        /// </summary>
        private static async Task ListSites(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var iisService = scope.ServiceProvider.GetRequiredService<IIISManagerService>();

            var sites = await iisService.GetSitesAsync();
            
            Console.WriteLine();
            Console.WriteLine("Available Sites:");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
            
            foreach (var site in sites.OrderBy(s => s))
            {
                Console.WriteLine($"  {site}");
            }
        }

        /// <summary>
        /// Triggers a manual deployment for a specific application
        /// </summary>
        private static async Task<int> TriggerDeployment(IHost host, string applicationName)
        {
            using var scope = host.Services.CreateScope();
            var orchestrationService = scope.ServiceProvider.GetService<IDeploymentOrchestrationService>();
            
            if (orchestrationService == null)
            {
                Console.WriteLine("Deployment orchestration service not available");
                return 1;
            }

            try
            {
                await orchestrationService.TriggerManualDeploymentAsync(applicationName);
                Console.WriteLine($"Manual deployment triggered for '{applicationName}'");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to trigger deployment for '{applicationName}': {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Runs the application in interactive mode
        /// </summary>
        private static async Task RunInteractiveMode(IHost host)
        {
            Console.WriteLine();
            Console.WriteLine("Starting deployment automation service...");
            Console.WriteLine("Press 'q' to quit, 'h' for help, or 'Ctrl+C' to stop");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine();
                Console.WriteLine("Shutdown requested...");
            };

            // Start the host
            var hostTask = host.RunAsync(cts.Token);

            // Handle user input
            var inputTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var key = Console.ReadKey(true);
                    
                    switch (key.KeyChar)
                    {
                        case 'q':
                        case 'Q':
                            cts.Cancel();
                            return;
                        
                        case 'h':
                        case 'H':
                            DisplayInteractiveHelp();
                            break;
                        
                        case 's':
                        case 'S':
                            await DisplaySystemStatus(host);
                            break;
                    }
                }
            }, cts.Token);

            // Wait for either the host to stop or user to quit
            await Task.WhenAny(hostTask, inputTask);
            
            if (!cts.Token.IsCancellationRequested)
            {
                cts.Cancel();
            }

            Console.WriteLine("Shutting down...");
        }

        /// <summary>
        /// Displays help information
        /// </summary>
        private static void DisplayHelp()
        {
            Console.WriteLine();
            Console.WriteLine("IIS Deployment Automation System - Command Line Options");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("Usage: IISDeploymentAutomation.exe [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --validate, -v              Validate system requirements and exit");
            Console.WriteLine("  --list-pools, -lp           List all application pools and their states");
            Console.WriteLine("  --list-sites, -ls           List all IIS sites");
            Console.WriteLine("  --deploy <app>, -d <app>    Trigger manual deployment for application");
            Console.WriteLine("  --help, -h, /?              Show this help message");
            Console.WriteLine();
            Console.WriteLine("Interactive Mode:");
            Console.WriteLine("  Run without arguments to start interactive monitoring mode");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  IISDeploymentAutomation.exe --validate");
            Console.WriteLine("  IISDeploymentAutomation.exe --deploy MainApplication");
            Console.WriteLine("  IISDeploymentAutomation.exe --list-pools");
            Console.WriteLine();
        }

        /// <summary>
        /// Displays interactive help
        /// </summary>
        private static void DisplayInteractiveHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Interactive Commands:");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
            Console.WriteLine("  h - Show this help");
            Console.WriteLine("  s - Show system status");
            Console.WriteLine("  q - Quit application");
            Console.WriteLine("  Ctrl+C - Stop service");
            Console.WriteLine();
        }

        /// <summary>
        /// Displays current system status
        /// </summary>
        private static async Task DisplaySystemStatus(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var fileMonitor = scope.ServiceProvider.GetRequiredService<IFileSystemMonitorService>();
            var orchestrationService = scope.ServiceProvider.GetService<IDeploymentOrchestrationService>();

            var currentOperations = orchestrationService != null ? 
                await orchestrationService.GetCurrentOperationsAsync() : 
                new List<DeploymentOperation>();

            Console.WriteLine();
            Console.WriteLine("System Status:");
            Console.WriteLine("─────────────────────────────────────────────────────────────────");
            Console.WriteLine($"  Service Status:      Running");
            Console.WriteLine($"  File Monitoring:     {(fileMonitor.IsMonitoring ? "Active" : "Inactive")}");
            Console.WriteLine($"  Active Deployments:  {currentOperations.Count}");
            Console.WriteLine($"  Current Time:        {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Uptime:              {DateTime.Now - Process.GetCurrentProcess().StartTime:hh\\:mm\\:ss}");
            Console.WriteLine();
        }
    }
}
