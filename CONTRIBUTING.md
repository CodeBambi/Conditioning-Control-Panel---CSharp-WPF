# Contributing to Conditioning Control Panel

Thank you for your interest in contributing! This document provides guidelines for contributing to the project.

## Development Setup

### Prerequisites
- Windows 10/11 (64-bit)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (Community edition is free) 
  - Or [VS Code](https://code.visualstudio.com/) with C# Dev Kit extension
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

### Getting Started

```bash
# Clone the repository
git clone https://github.com/CodeBambi/Conditioning-Control-Panel.git
cd Conditioning-Control-Panel

# Restore packages
dotnet restore

# Build
dotnet build

# Run
dotnet run --project ConditioningControlPanel
```

### Project Structure

```
ConditioningControlPanel/
├── Models/           # Data models (AppSettings, ProgressionData)
├── Services/         # Business logic
│   ├── SettingsService.cs      # Settings management
│   ├── AudioService.cs         # Audio playback
│   ├── FlashService.cs         # Image flashing
│   ├── VideoService.cs         # Video playback
│   ├── ProgressionService.cs   # XP/leveling
│   ├── SchedulerService.cs     # Scheduling
│   └── SecurityHelper.cs       # Security utilities
├── ViewModels/       # MVVM ViewModels
├── Views/            # WPF Windows/Controls
├── Themes/           # XAML styles
└── Resources/        # Icons, assets
```

## Making Changes

### Branch Naming
- `feature/description` - New features
- `fix/description` - Bug fixes
- `docs/description` - Documentation
- `refactor/description` - Code refactoring

### Commit Messages
Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add bubble pop mini-game
fix: audio ducking not restoring volume
docs: update README installation steps
refactor: extract SecurityHelper from SettingsService
```

### Pull Request Process

1. **Fork** the repository
2. **Create** a feature branch from `main`
3. **Make** your changes
4. **Test** thoroughly
5. **Submit** a pull request

### PR Checklist
- [ ] Code follows existing style
- [ ] Changes are tested
- [ ] Documentation updated if needed
- [ ] No new warnings
- [ ] PR description explains changes

## Code Style

### C# Guidelines
- Use C# 12 features where appropriate
- Follow Microsoft's [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable/method names
- Add XML documentation for public APIs
- Use `async/await` for I/O operations

### XAML Guidelines
- Use styles from `Themes/CustomStyles.xaml`
- Keep XAML clean and readable
- Use data binding (no code-behind logic for UI updates)

### Security Guidelines
- Validate all user inputs
- Use `SecurityHelper` for path operations
- Never log sensitive data
- Handle exceptions gracefully

## Testing

Currently, the project doesn't have automated tests. Contributions adding tests are welcome!

### Manual Testing
Before submitting:
1. Test all affected features
2. Test with different settings combinations
3. Verify no regressions in existing functionality
4. Test edge cases (empty folders, missing files, etc.)

## Reporting Issues

### Bug Reports
Include:
- Windows version
- .NET version
- Steps to reproduce
- Expected vs actual behavior
- Logs from `logs/app.log` (sanitize personal info)

### Feature Requests
- Describe the feature
- Explain the use case
- Suggest implementation if possible

## Questions?

- Open a [Discussion](../../discussions)
- Check existing [Issues](../../issues)

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
