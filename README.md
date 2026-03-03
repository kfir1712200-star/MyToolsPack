# MyTools – Unity Editor Tools Package

A collection of Unity Editor tools for game developers.

## Tools Included

| Tool | Shortcut | Description |
|------|----------|-------------|
| **AI Debug Assistant** | `Ctrl+Shift+D` | Captures Unity console errors and uses OpenAI to explain & suggest fixes |
| **Auto Level Generator** | `Ctrl+Shift+L` | Procedurally generates levels for Match-3, Sort/Stack, and Maze games with JSON export |
| **Build & Store Automation** | `Ctrl+Shift+B` | One-click multi-platform builds with auto-versioning and pre-build validation |
| **Performance Analyzer** | `Ctrl+Shift+P` | Scans your project and scene for performance issues with actionable recommendations |

## Installation

### Option A – Local Disk (fastest)

1. Copy the `MyToolsPackage` folder anywhere on your machine
2. In Unity, open **Window > Package Manager**
3. Click **+** → **Add package from disk...**
4. Select `MyToolsPackage/package.json`

### Option B – Git URL (share with a team)

1. Push this folder to a Git repository
2. In Unity, open **Window > Package Manager**
3. Click **+** → **Add package from git URL...**
4. Enter your repository URL (e.g. `https://github.com/yourname/mytools.git`)

## Requirements

- Unity 2021.3 or newer
- For **AI Debug Assistant**: an OpenAI API key (enter it in the tool's Settings panel)

## Usage

Open any tool from the **MyTools** menu in the Unity Editor top menu bar.
