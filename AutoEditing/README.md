# Sniper Montage Automation for VEGAS Pro

An automated system for creating sniper montages from Call of Duty clips, with music synchronization and advanced time remapping using VEGAS Pro's scripting API.

## Project Structure

```
Core/
├── Domain/                     # Domain-driven design structure
│   ├── Audio/                 # VEGAS-free audio analysis (testable outside VEGAS)
│   │   ├── AudioLoader.cs     # Decodes songs/clip audio to mono PCM via Media Foundation
│   │   ├── BeatDetector.cs    # Real tempo + beat grid detection (onset autocorrelation)
│   │   └── ShotDetector.cs    # Detects sniper shots as loud hard-attack transients
│   ├── Editing/               # Video editing and effects components
│   │   ├── EffectsApplier.cs  # Applies effects like time remapping, shake, name tags
│   │   ├── MontageOrchestrator.cs # Runs the full pipeline
│   │   ├── MontagePlanner.cs  # VEGAS-free planning: cuts on beats, kills on beats
│   │   └── TimelineBuilder.cs # Executes the plan on the VEGAS timeline
│   ├── Clip/                  # Clip processing and validation
│   │   ├── Clip.cs           # Data model for video clips with metadata
│   │   ├── ClipParser.cs     # Parses clip filenames to extract metadata
│   │   └── ClipValidator.cs  # Validates clip quality and format
├── Scripts/
│   ├── EntryPoint.cs         # VEGAS Pro script entry point (loads the dock view)
│   └── Ui/
│       ├── MontageDockView.cs # Docked window UI (DockableControl)
│       └── VegasTheme.cs      # VEGAS Pro 20 dark theme palette and control styling
└── Properties/
    └── AssemblyInfo.cs       # Assembly information

Tools/
└── AnalysisHarness/           # Console app: runs parse/beat/shot/plan against real
                               # clips without VEGAS. Also has --debug-tempo and
                               # --debug-shots commands for tuning the detectors.
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
- **Clip Parsing**: Automatically parses clip filenames to extract metadata (player, game, map, gun, type, sequence, notes)
- **Clip Validation**: Validates video files for quality (FPS >= 60, format compatibility)
- **Beat Detection**: Real tempo and beat-grid detection from the song's audio (onset envelope + autocorrelation, octave-folded to a musical cutting tempo)
- **Shot Detection**: Real sniper-shot detection from each clip's audio track (loud hard-attack transients over the clip's ambient loudness)
- **Montage Planning**: Every cut lands on a beat, and each clip's first shot lands exactly on a beat a fixed lead-in after the cut
- **Timeline Building**: Executes the plan on the VEGAS timeline with trimmed source windows (take offsets)
- **Time Remapping / Effects**: Framework for velocity envelopes, name tags, color correction (still placeholder)
- **User Interface**: Docked VEGAS window (DockableControl) styled to match the VEGAS Pro 20 dark theme, with multiple creation modes

### Clip Naming Convention
Clips are named with dash-separated sections, with the gun/type details packed
into the final section:
```
PlayerName - Game - Map - GUN [TYPE...] [SEQUENCE] [(notes)].mp4
```

- **GUN**: first word of the details section (e.g. `MORS`, `XRK`, `KATT`, `Signal`)
- **TYPE**: any words after the gun (e.g. `6ON`, `QUAD`, `5ON Triple`, `Airborne Triple`)
- **SEQUENCE**: an optional zero-padded counter (`001`, `002`)
- **(notes)**: optional free text in parentheses (e.g. `(7mult)`, `(same game)`)
- Montage placement is marked only by the `[OPENER]`/`[CLOSER]` filename
  prefixes (see the legacy convention below). Words in the details section
  such as `Ender` (a game-ending kill) are just part of the clip type and do
  not affect placement.

**Examples:**
```
Glovali - MWIII - Dome - MORS 6ON 001.mp4
Glovali - MWIII - Greece - XRK QUAD.mp4
Glovali - MWIII - AFGHAN - MORS 5ON Triple Ender (7mult).mp4
Glovali - MWIII - Rio - KATT 5ON X2 001 (Triple).mp4
```

The legacy convention (`[OPENER]Player - Game - Map - Gun - Type - 001.mp4`)
is still supported.

### Analysis Harness
The VEGAS-free part of the pipeline (parsing, beat detection, shot detection,
planning) can be run from the command line without VEGAS Pro:
```bash
dotnet build Tools/AnalysisHarness
Tools/AnalysisHarness/bin/Debug/net48/AnalysisHarness.exe <clipsFolder> [songPath]

# Detector tuning helpers:
AnalysisHarness.exe --debug-tempo <songPath>   # ranked tempo candidates + grid fit
AnalysisHarness.exe --debug-shots <clipPath>   # loudest envelope peaks with attack stats
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
2. **Run the script** from Tools → Scripting → Run Script — the *Sniper Montage Creator* opens as a docked window (re-running the script re-activates the existing pane; it can be floated or re-docked like any other VEGAS window)
3. **Select your clips folder** containing properly named MP4 files
4. **Select your background music** (MP3, WAV, M4A, AAC supported)
5. **Choose creation mode**:
   - **Full Montage**: Complete processing with validation and effects
   - **Quick Montage**: Fast processing for quick previews
6. **Monitor progress** in the log window
7. **Review the generated timeline** in VEGAS Pro

## Development Notes

### Current Limitations
- **Shot vs. kill**: the shot detector finds the player's loud shots/impacts; it
  cannot distinguish a hit from a miss, so clips with lots of firing detect more
  "shots" than actual kills. Alignment uses the first shot, which is usually right.
- **Time Remapping**: Framework implemented but VelocityEnvelope API requires further VEGAS Pro API research
- **Effect Plugins**: Some effects (shake, color correction) are placeholder implementations pending VEGAS API exploration
- **Audio decoding** uses Windows Media Foundation via NAudio (NAudio.Core +
  NAudio.Wasapi in the `packages` folder), so analysis is Windows-only.

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
