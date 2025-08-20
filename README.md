# Confluence Exporter

A powerful .NET application that integrates with the Atlassian Marketplace to export Confluence pages, spaces, or hierarchies into Markdown files.

## Features

- **Multiple Export Modes**: Export individual pages, entire spaces, page hierarchies, or all accessible spaces
- **Markdown Conversion**: Convert Confluence HTML content to clean Markdown with support for:
  - Code blocks and syntax highlighting
  - Tables and formatting
  - Images and attachments
  - Confluence macros (info panels, code blocks, etc.)
- **Atlassian Marketplace Integration**: Built-in support for marketplace app registration and usage metrics
- **Concurrent Processing**: Configurable concurrent requests for faster exports
- **Hierarchy Preservation**: Maintain folder structure based on page hierarchy
- **Rich Metadata**: Include page metadata, version information, and timestamps
- **Asset Downloads**: Automatically download and organize images and attachments

## Prerequisites

- .NET 9.0 or later
- Confluence API access (API token required)
- Valid Confluence instance (Cloud or Server)

## Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd confluence-exporter
```

2. Build the application:
```bash
dotnet build
```

3. Publish for distribution (optional):
```bash
dotnet publish -c Release -o ./publish
```

## Configuration

The application uses command-line parameters for configuration. No configuration file is required.

### Required Parameters

- `--command` - The operation to perform (page, space, hierarchy, all, list-spaces)
- `--base-url` or `-u` - Your Confluence base URL (e.g., `https://company.atlassian.net`)
- `--username` or `-n` - Your Confluence username or email
- `--token` or `-t` - Your Confluence API token
- `--target` - Target ID (page ID or space key) - required for `page`, `space`, and `hierarchy` commands

### Optional Parameters

- `--output` or `-o` - Output directory (default: `output`)
- `--format` or `-f` - Export format: `Markdown`, `Html`, or `Both` (default: `Markdown`)
- `--concurrent` or `-c` - Maximum concurrent requests (default: `5`)
- `--delay` or `-d` - Request delay in milliseconds (default: `100`)
- `--preserve-hierarchy` - Preserve page hierarchy in folder structure (default: `true`)
- `--include-images` - Download and include images (default: `true`)
- `--include-attachments` - Download and include attachments (default: `true`)
- `--create-index` - Create index files (default: `true`)
- `--verbose` or `-v` - Enable verbose logging
- `--include-spaces` - Only export these spaces (comma-separated space keys) - for `all` command
- `--exclude-spaces` - Exclude these spaces (comma-separated space keys) - for `all` command

### Getting Help

Use the help parameter to see all available options:

```bash
# Show help information
dotnet run -- --help
# or
dotnet run -- -?
```

## Usage

The application uses FluentCommandLineParser for command-line argument parsing with a flag-based approach.

### Available Commands

Use the `--command` parameter to specify the operation:

- `page` - Export a single page and its children (requires `--target` with page ID)
- `space` - Export an entire space (requires `--target` with space key)  
- `hierarchy` - Export a page hierarchy (requires `--target` with page ID)
- `all` - Export all accessible spaces
- `list-spaces` - List all accessible spaces

### Command Examples

```bash
# Export a single page
dotnet run -- --command page --target <page-id> --base-url "https://company.atlassian.net" --username "user@company.com" --token "your-api-token"

# Export an entire space
dotnet run -- --command space --target <space-key> --base-url "https://company.atlassian.net" --username "user@company.com" --token "your-api-token"

# Export page hierarchy
dotnet run -- --command hierarchy --target <root-page-id> --base-url "https://company.atlassian.net" --username "user@company.com" --token "your-api-token"

# Export all spaces
dotnet run -- --command all --base-url "https://company.atlassian.net" --username "user@company.com" --token "your-api-token"

# Export all spaces with filtering
dotnet run -- --command all --include-spaces "SPACE1,SPACE2" --exclude-spaces "TEMP,ARCHIVED" --base-url "https://company.atlassian.net" --username "user@company.com" --token "your-api-token"

# List available spaces
dotnet run -- --command list-spaces --base-url "https://company.atlassian.net" --username "user@company.com" --token "your-api-token"

# Export with additional options
dotnet run -- --command space --target MYSPACE --base-url "https://company.atlassian.net" --username "user@company.com" --token "your-api-token" --output "./export" --format Both --concurrent 3 --delay 200 --verbose
```

### Short Form Parameters

FluentCommandLineParser supports both long and short forms for many parameters:

```bash
# Using short form parameters
dotnet run -- --command space --target MYSPACE -u "https://company.atlassian.net" -n "user@company.com" -t "your-api-token" -o "./export" -f Markdown -c 3 -d 200 -v
```

## Output Structure

The exporter creates a structured output directory:

```
output/
├── README.md                 # Global index (when exporting multiple spaces)
├── Space Name/
│   ├── INDEX.md             # Space index
│   ├── Page Title/
│   │   ├── README.md        # Page content in Markdown
│   │   ├── index.html       # Page content in HTML (if enabled)
│   │   └── assets/          # Images and attachments
│   │       ├── image1.png
│   │       └── document.pdf
│   └── Another Page/
│       └── ...
```

### Markdown Format

Each exported page includes metadata in YAML front matter:

```markdown
---
title: Page Title
id: 123456
type: page
status: current
space: SPACE_KEY
version: 5
last_modified: 2023-12-01 14:30:00
---

# Page Title

Page content converted to Markdown...
```

## API Token Setup

1. Go to your Atlassian account settings: https://id.atlassian.com/manage-profile/security/api-tokens
2. Click "Create API token"
3. Provide a descriptive label
4. Copy the generated token
5. Use the token with the `--token` parameter

## Atlassian Marketplace Integration

This application includes built-in support for Atlassian Marketplace integration:

- **App Registration**: Automatically register your app with the marketplace
- **Usage Metrics**: Send usage statistics for analytics
- **Installation Validation**: Validate marketplace installations

The marketplace integration is designed to be used when distributing this application as an official Atlassian Marketplace app.

## Error Handling

The application includes comprehensive error handling:

- **Retry Logic**: Automatic retry with exponential backoff for transient failures
- **Circuit Breaker**: Prevents cascade failures during extended outages  
- **Rate Limiting**: Respects API rate limits with configurable delays
- **Detailed Logging**: Comprehensive logging for troubleshooting

## Limitations

- **API Rate Limits**: Confluence enforces API rate limits. Use `--delay` to adjust request timing
- **Large Exports**: Very large spaces may take significant time to export
- **Macro Support**: Some complex Confluence macros may not convert perfectly to Markdown
- **Permissions**: You can only export content you have read access to

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For issues and questions:

1. Check the troubleshooting section below
2. Search existing GitHub issues
3. Create a new issue with detailed information

### Troubleshooting

**Authentication Issues:**
- Verify your API token is correct and not expired
- Ensure your username/email is correct
- Check that your account has access to the Confluence instance

**Export Issues:**
- Verify page IDs and space keys are correct
- Check your permissions on the content
- Use `--verbose` for detailed logging

**Performance Issues:**
- Reduce `--concurrent` value for slower instances
- Increase `--delay` for rate-limited environments
- Consider exporting smaller scopes (individual spaces vs. all spaces)