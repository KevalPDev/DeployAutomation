<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

# IIS Deployment Automation System - Copilot Instructions

## Project Overview
This is an enterprise-grade .NET Core application for automating IIS deployments with comprehensive application pool management, file system monitoring, and audit logging.

## Architecture Guidelines
- Follow enterprise-level coding standards and patterns
- Use dependency injection for all services
- Implement comprehensive error handling and logging
- Ensure all operations are asynchronous where applicable
- Follow SOLID principles and clean code practices

## Key Technologies
- .NET 8.0
- Microsoft.Web.Administration for IIS management
- Serilog for structured logging
- Microsoft.Extensions.Hosting for background services
- FileSystemWatcher for real-time monitoring

## Code Style Preferences
- Use explicit typing where beneficial for readability
- Include comprehensive XML documentation for all public members
- Prefer async/await over synchronous operations
- Use meaningful variable and method names
- Include detailed logging for all operations

## Security Considerations
- Always validate permissions before operations
- Use SecurityUtils for all security-related checks
- Log all security validation attempts
- Handle sensitive configuration data appropriately
- Ensure proper exception handling without exposing internal details

## Performance Guidelines
- Use semaphores and locks appropriately for thread safety
- Implement batching for file system operations
- Use cancellation tokens for all async operations
- Monitor and limit concurrent operations
- Implement proper disposal patterns

## Testing Approach
- Write unit tests for all business logic
- Mock external dependencies (IIS, file system)
- Test error scenarios and edge cases
- Validate permission checking thoroughly
- Test configuration validation

## Logging Standards
- Use structured logging with Serilog
- Include context information in all log entries
- Log operation start/end with timing
- Use appropriate log levels (Trace, Debug, Information, Warning, Error, Critical)
- Include correlation IDs for tracking operations

## Error Handling
- Use specific exception types
- Provide meaningful error messages
- Log exceptions with full context
- Implement retry logic where appropriate
- Gracefully handle partial failures

## Configuration Management
- Validate all configuration on startup
- Provide sensible defaults
- Support environment-specific overrides
- Document all configuration options
- Implement configuration hot-reload where applicable

## Deployment Considerations
- Ensure application runs with proper permissions
- Validate system requirements on startup
- Provide clear error messages for configuration issues
- Support both interactive and service modes
- Include health checks and monitoring
