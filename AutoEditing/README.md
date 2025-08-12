# Sniper Montage Automation for VEGAS Pro

An automated system for creating sniper montages from Call of Duty clips, with music synchronization and advanced time remapping using VEGAS Pro's scripting API.

## Project Structure

```
Core/
├── Domain/                     # Domain-driven design structure
│   ├── Editing/               # Video editing and effects components
│   │   ├── BeatDetector.cs    # Detects beats in music (MVP implementation)
│   │   ├── EffectsApplier.cs  # Applies effects like time remapping, shake, name tags
│   │   ├── MontageOrchestrator.cs # Main orchestrator class (renamed from MontageCreator)
│   │   └── TimelineBuilder.cs # Builds the VEGAS timeline from clips
│   ├── Clip/                  # Clip processing and validation
│   │   ├── Clip.cs           # Data model for video clips with metadata
│   │   ├── ClipParser.cs     # Parses clip filenames to extract metadata
│   │   ├── ClipValidator.cs  # Validates clip quality and format
│   │   └── KillDetector.cs   # Detects kill moments in clips (MVP implementation)
│   ├── Clip.cs               # Legacy clip model (maintained for compatibility)
│   └── Resolution.cs         # Video resolution utilities
├── Scripts/
│   └── EntryPoint.cs         # VEGAS Pro script entry point with UI
├── Properties/
│   └── AssemblyInfo.cs       # Assembly information
└── TimelineManager.cs        # Legacy timeline utilities
```

## Architecture Overview

The project follows a **Domain-Driven Design (DDD)** approach with clear separation of concerns:

### Domain.Editing
Contains all video editing and timeline manipulation logic:
- **MontageOrchestrator**: Main workflow orchestration
- **TimelineBuilder**: VEGAS timeline construction and clip arrangement
- **EffectsApplier**: Visual effects, time remapping, and post-processing
- **BeatDetector**: Music analysis and beat synchronization

### Domain.Clip
Handles all clip-related operations:
- **Clip**: Core data model with metadata and properties
- **ClipParser**: Intelligent filename parsing with convention support
- **ClipValidator**: Quality assurance and format validation
- **KillDetector**: Content analysis for highlight detection

## Features

### Implemented Features
- **Clip Parsing**: Automatically parses clip filenames to extract metadata (player, game, map, gun, type, sequence)
- **Clip Validation**: Validates video files for quality (FPS >= 60, format compatibility)
- **Timeline Building**: Automatically arranges clips on VEGAS timeline with proper sorting
- **Time Remapping**: Framework for velocity envelopes and slow-motion effects (MVP placeholder)
- **Beat Detection**: Basic beat detection for music synchronization (MVP implementation)
- **Kill Detection**: Placeholder kill detection in video clips (MVP implementation)
- **Effects Application**: Shake effects, name tags, and color correction frameworks
- **User Interface**: Windows Forms UI with multiple creation modes

### Clip Naming Convention
Clips should be named in the following format:
```
[PREFIX]PlayerName - Game - Map - Gun - ClipType - SequenceNumber.mp4
```

**Prefixes:**
- `[OPENER]` - Clips that should appear at the beginning
- `[CLOSER]` - Clips that should appear at the end
- No prefix - Regular clips

**Example:**
```
[OPENER]SniperPro - MW2 - Rust - Intervention - Quickscope - 001.mp4
PlayerName - Warzone - Verdansk - HDR - Longshot - 015.mp4
[CLOSER]EndGame - MW3 - Terminal - Barrett - Collateral - 999.mp4
```

## Installation

1. **Prerequisites**:
   - VEGAS Pro 20.0 or later
   - .NET Framework 4.8
   - Visual Studio 2019/2022 (for development)

2. **Build the Project**:
   ```bash
   MSBuild Core\Core.csproj
   ```

3. **Deploy to VEGAS Pro**:
   - Copy `Core.dll` to your VEGAS Pro script folder:
     - `%PROGRAMDATA%\VEGAS Pro\Scripts\` (for all users)
     - `%APPDATA%\VEGAS Pro\Scripts\` (for current user)
   - Or use Application Extensions folder for auto-loading

## Usage

1. **Launch VEGAS Pro** and create a new project
2. **Run the script** from Tools → Scripting → Run Script
3. **Select your clips folder** containing properly named MP4 files
4. **Select your background music** (MP3, WAV, M4A, AAC supported)
5. **Choose creation mode**:
   - **Full Montage**: Complete processing with validation and effects
   - **Quick Montage**: Fast processing for quick previews
6. **Monitor progress** in the log window
7. **Review the generated timeline** in VEGAS Pro

## Development Notes

### MVP Limitations
- **Beat Detection**: Currently uses placeholder timing (120 BPM). Real implementation would use FFT analysis or external audio libraries like NAudio
- **Kill Detection**: Uses placeholder kill timing. Real implementation would analyze audio waveforms for gunshot detection
- **Time Remapping**: Framework implemented but VelocityEnvelope API requires further VEGAS Pro API research
- **Effect Plugins**: Some effects (shake, color correction) are placeholder implementations pending VEGAS API exploration

### Future Enhancements
- Advanced audio analysis for precise beat and kill detection
- Complete VelocityEnvelope implementation for time remapping effects
- Machine learning for automatic highlight detection
- Integration with external video analysis tools
- Support for more video formats and codecs
- Advanced color grading and cinematic effects
- Automatic thumbnail generation
- Export presets and render queue automation

### API Usage
This project uses the VEGAS Pro Scripting API (`ScriptPortal.Vegas`). Key classes used:
- `Vegas` - Main application object
- `Project` - Current project
- `VideoTrack`, `AudioTrack` - Timeline tracks
- `VideoEvent`, `AudioEvent` - Timeline events
- `Media`, `MediaPool` - Media management
- `Take` - Media takes for events
- `Timecode` - Time representation

### Architecture Benefits
The new Domain-Driven Design structure provides:
- **Separation of Concerns**: Clear boundaries between editing and clip operations
- **Maintainability**: Organized code structure for easier development
- **Extensibility**: Easy to add new features within appropriate domains
- **Testability**: Isolated components for unit testing
- **SOLID Principles**: Following object-oriented design principles

## Configuration

### Supported File Formats
- **Video**: MP4 (H.264/H.265), AVI, MOV
- **Audio**: MP3, WAV, M4A, AAC

### Quality Requirements
- **Minimum FPS**: 60 (configurable in ClipValidator)
- **Recommended Resolution**: 1080p or higher
- **Bitrate**: Automatic validation (placeholder)

## Troubleshooting

### Common Issues
1. **"No clips found"**: Check clip naming convention and file extensions
2. **"Could not import song"**: Verify audio file format and codec
3. **"Validation failed"**: Check video quality, FPS, and file integrity
4. **API errors**: Ensure VEGAS Pro version compatibility (20.0+)

### Debug Mode
Enable debug output by checking the log window. All errors and processing steps are logged for troubleshooting.

## Development Setup

### Prerequisites
- **VEGAS Pro 20.0+** installed
- **Visual Studio Code** (recommended IDE)
- **.NET Framework 4.8** or higher

### Required VS Code Extensions
When you open this project in VS Code, you'll be prompted to install the recommended extensions. Please install:

- **EditorConfig for VS Code** (`editorconfig.editorconfig`) - **REQUIRED** for code style enforcement
- **C#** (`ms-dotnettools.csharp`) - **REQUIRED** for C# language support
- **C# Dev Kit** (`ms-dotnettools.csdevkit`) - **RECOMMENDED** for enhanced C# development features

To install the recommended extensions:
1. Open the project in VS Code
2. When prompted, click "Install" on the extension recommendations notification
3. Or manually install via Command Palette: `Ctrl+Shift+P` → "Extensions: Show Recommended Extensions"

### Code Style Rules
This project enforces strict code styling rules via EditorConfig:
- **No `var` keyword usage** - explicit types are required
- Consistent indentation and formatting
- C# naming conventions enforcement

The EditorConfig extension will show real-time style violations and provide automatic fixes.

### Building the Project
```bash
# Build the Core project
dotnet build Core/Core.csproj --configuration Debug

# Or use the VS Code task
Ctrl+Shift+P → "Tasks: Run Task" → "Build Core Project"
```

### Development Workflow
1. Make your changes following the established code style
2. Build the project to ensure no style violations
3. Test with VEGAS Pro using the DLL copy task
4. Submit pull requests with clean, styled code

## License

This project is for educational and personal use. VEGAS Pro and its scripting API are trademarks of VEGAS Creative Software.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Implement your changes
4. Test with VEGAS Pro
5. Submit a pull request

For questions or support, please create an issue in the repository.
