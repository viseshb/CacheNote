# CacheNote - Product Requirements Document (PRD)

Version: 1.0

Platform: Windows 11+

Project Type: Premium Desktop Productivity Application

---

# Product Vision

Build a premium desktop notes and reminders application called **CacheNote**.

The application combines:

* Apple Notes simplicity
* Windows Sticky Notes convenience
* Modern productivity workflows
* Native Windows 11 design language

CacheNote should feel like a polished commercial application rather than a student project.

The application must be:

* Fast
* Beautiful
* Minimalistic
* Smooth
* Native-feeling
* Offline-first

Users should be able to keep CacheNote open all day without it feeling intrusive or consuming significant system resources.

---

# Technology Stack

## Core Framework

Language:

* C# 13

Runtime:

* .NET 9

UI Framework:

* WinUI 3

Architecture:

* MVVM

MVVM Toolkit:

* CommunityToolkit.Mvvm

Dependency Injection:

* Microsoft.Extensions.DependencyInjection

Configuration:

* Microsoft.Extensions.Configuration

Logging:

* Microsoft.Extensions.Logging

---

# Data Storage

## Storage Strategy

Application must be fully offline.

No cloud.

No accounts.

No authentication.

No external servers.

---

## Database

Database Engine:

* SQLite

Reason:

* Single local file
* No server required
* Fast
* Reliable
* Easy backup

---

## Local Data Structure

CacheNote/

├── CacheNote.exe

├── data/
│   └── CacheNote.db

├── attachments/
│   ├── image1.png
│   ├── image2.jpg
│   └── ...

├── config/
│   └── settings.json

├── logs/
│   └── app.log

All user data must remain inside the application folder.

Portable mode should be supported.

Moving the entire folder to another computer should preserve all notes and settings.

---

# Design Philosophy

## Design Inspiration

Reference applications:

* Apple Notes
* Notion
* Arc Browser
* Linear
* Modern Windows 11 apps

Avoid:

* Windows XP appearance
* Traditional enterprise software
* Outdated desktop UI patterns
* Excessive gradients
* Neon colors
* Cluttered interfaces

---

# Design Goals

The UI must feel:

* Premium
* Calm
* Modern
* Minimal
* Professional
* Responsive

Focus on:

* Typography
* White space
* Layout balance
* Smooth animations
* Subtle visual hierarchy

---

# Windows 11 Design System

Use:

* Fluent Design
* Fluent UI Controls
* Fluent Icons
* Mica Material
* Acrylic Effects where appropriate

Use native WinUI styling whenever possible.

Avoid custom styling unless it improves the experience.

---

# Theme System

## Light Theme

Background:
#FAFAF8

Surface:
#FFFFFF

Border:
#E4E4E7

Primary Text:
#18181B

Secondary Text:
#71717A

Accent:
#2563EB

---

## Dark Theme

Background:
#121212

Surface:
#1E1E1E

Border:
#2A2A2A

Primary Text:
#F5F5F5

Secondary Text:
#A1A1AA

Accent:
#60A5FA

---

# Window Behavior

## Resizable

Window must be freely resizable.

---

## Persist Window State

Save:

* Width
* Height
* Position
* Theme
* Sidebar state

Restore on startup.

---

## Always On Top

Provide a prominent toggle.

When enabled:

* Window remains above all applications

When disabled:

* Standard Windows behavior

State must persist.

---

## Compact Mode

Provide a compact floating mode.

Example size:

300 x 300

Useful for quick note-taking.

---

## Dock Mode

Support:

* Left Dock
* Right Dock

Window snaps to screen edge and remains accessible while working.

---

# Startup Integration

Settings option:

Launch CacheNote when Windows starts.

Requirements:

* Enable startup
* Disable startup
* Persist preference

---

# System Tray Integration

Application should continue running in system tray.

Closing the window should:

Hide application.

Do not exit.

---

## Tray Menu

Options:

* Open CacheNote
* New Note
* New Task
* Toggle Always On Top
* Pause Notifications
* Settings
* Exit

---

# Notes System

## Notes

Users can:

* Create notes
* Edit notes
* Duplicate notes
* Archive notes
* Delete notes

Unlimited notes.

---

## Rich Text Editor

Support:

* Bold
* Italic
* Underline
* Headings
* Bullets
* Numbered lists
* Checklists

Auto-save all changes.

No save button.

---

## Favorites

Allow starring notes.

Favorites appear in dedicated section.

---

## Pinned Notes

Allow pinning important notes to top.

---

# Attachments

Supported formats:

* PNG
* JPG
* JPEG
* WEBP

Features:

* Drag and drop
* Clipboard paste
* Image preview
* Remove attachment

Store images locally.

---

# Tasks

Notes may be converted into tasks.

Task fields:

* Title
* Description
* Due Date
* Priority

Priority levels:

* Low
* Medium
* High

---

# Reminder System

Users can attach reminders to notes or tasks.

Reminder includes:

* Date
* Time
* Message
* Repeat schedule

---

## Repeat Options

* Once
* Daily
* Weekly
* Monthly

---

# Notifications

Use native Windows notifications.

Notification actions:

* Open Note
* Mark Complete
* Snooze 5 Minutes
* Snooze 15 Minutes

Notifications must work when application is minimized to tray.

---

# Calendar View

Views:

* Month
* Week

Display:

* Tasks
* Reminders
* Upcoming events

---

# Search

Global search.

Search:

* Note titles
* Note content
* Tags
* Tasks

Search results should update instantly.

---

# Tags

Users can:

* Create tags
* Rename tags
* Delete tags

Examples:

* Work
* School
* Personal
* Important

---

# Keyboard Shortcuts

Default shortcuts:

Ctrl + Shift + N

Create Quick Note

---

Ctrl + F

Search

---

Ctrl + P

Toggle Always On Top

---

Ctrl + D

Duplicate Note

---

Shortcuts should be configurable.

---

# Main Layout

Three-column layout.

---

## Sidebar

Contains:

* All Notes
* Favorites
* Tasks
* Reminders
* Calendar
* Settings

Collapsible.

---

## Notes List

Displays:

* Search
* Filters
* Note cards

---

## Editor

Displays:

* Selected note
* Attachments
* Reminder controls

---

# Animation Requirements

Target:

60 FPS

Use WinUI composition animations.

---

Animation Types

* Fade
* Scale
* Hover
* Slide

---

Animation Duration

150ms - 250ms

---

Avoid:

* Bouncy effects
* Excessive motion
* Flashy transitions

Animations should feel subtle and premium.

---

# Accessibility

Support:

* Keyboard navigation
* Screen readers
* High contrast mode
* Adjustable font size

---

# Database Tables

notes

tasks

attachments

reminders

tags

note_tags

favorites

settings

---

# Performance Targets

Cold Startup:
< 1.5 seconds

Memory Usage:
< 150 MB

Search:
Instant

UI:
60 FPS

Autosave:
Immediate

---

# Installer

Generate:

CacheNoteSetup.exe

Installer must:

* Install application
* Create Start Menu shortcut
* Create Desktop shortcut
* Register uninstaller

---

# Future Architecture

Design system should support future additions:

* OCR
* Voice Notes
* AI Summaries
* Cloud Sync
* Mobile Companion App
* Markdown Mode

Do not implement now.

Only prepare extensible architecture.

---

# Deliverables

Generate:

1. Complete solution architecture
2. Folder structure
3. MVVM implementation
4. SQLite schema
5. Fluent Design System
6. Reusable UI components
7. Reminder engine
8. System tray support
9. Startup integration
10. Native Windows notifications
11. Installer configuration
12. Settings management
13. Attachment management
14. Search engine

Final result should feel comparable to a modern premium productivity application released in 2026 for Windows 11.



## IMPORTANT INSTRUCTION: 

"Do not create generic Microsoft-demo-app styling. Create a premium, minimal productivity aesthetic inspired by Apple Notes, Linear, Arc, and Notion while still using native WinUI controls and Fluent Design"