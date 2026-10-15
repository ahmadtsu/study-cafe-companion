# Attribution & Third-Party Notices

This project incorporates design concepts, UI Automation taskbar layout probing, taskbar child window docking, and theme tokens adapted from:

## EverythingToolbar
- **Author**: Stephan Rumswinkel and contributors
- **License**: MIT License (Copyright © 2023 Stephan Rumswinkel)
- **Repository**: https://github.com/srwi/EverythingToolbar

Specific concepts adapted from EverythingToolbar:
- Window docking as `WS_CHILD` into `Shell_TrayWnd`
- Taskbar UI Automation probing (`TaskbarLayoutProbe`)
- Taskbar gliding motion (`TaskbarWindowAnimator`)
- Windows 11 Light and Dark theme color and geometry tokens (`SearchButtonCornerRadius`, `SearchButtonBackground*`)
- Windows 11 context menu styling
