# Natural Behavior in Tidal Plugin

## Overview

The Tidal plugin includes a sophisticated Natural Behavior feature designed to simulate human-like listening patterns when downloading music from Tidal. This helps avoid triggering Tidal's rate limiting or anti-abuse systems by making the download activity appear more natural.

## Key Features

- **Session Management**: Creates natural listening sessions with breaks between them
- **Track-to-Track Delays**: Adds realistic delays between downloading tracks in an album
- **Album-to-Album Delays**: Adds longer delays when switching between albums
- **Sequential Play**: Option to download tracks in sequential order, mimicking normal listening
- **Time-of-Day Adaptation**: Different download patterns based on time of day
- **Natural Organization**: Preserves album context and artist grouping
- **High Volume Handling**: Special modes for large download jobs

## Setup Guide

To properly enable natural behavior:

1. In Lidarr, go to **Settings → Download Clients → Tidal**
2. Under the **Natural Behavior** section, enable **Enable Natural Behavior**
3. Select a behavior profile or use Custom settings:
   - **Balanced**: Moderate delays suitable for regular use
   - **Casual Listener**: Longer delays, mimicking occasional listening
   - **Music Enthusiast**: Shorter delays, more active listening
   - **Custom**: Configure all parameters manually

## Recommended Settings

For best results with maintaining natural behavior:

- Set **Session Duration** between 20-45 minutes
- Set **Break Duration** between 10-30 minutes
- Enable **Simulate Listening Patterns**
- Set **Track-to-Track Delay Min/Max** to reasonable values (e.g. 3-10 seconds)
- Set **Album-to-Album Delay Min/Max** to longer values (e.g. 15-60 seconds)
- Enable **Complete Albums** and **Sequential Track Order** for the most natural behavior
- Set a reasonable **Max Concurrent Track Downloads** (2-3 is most natural)
- Set a conservative **Max Tracks Downloads Per Hour** (between 60-120)
- Consider enabling **Time-of-Day Adaptation** with appropriate active hours

## Troubleshooting

If you're still experiencing rate limiting from Tidal:

1. Decrease **Max Tracks Downloads Per Hour**
2. Increase the minimum and maximum delay values
3. Lower **Max Concurrent Track Downloads** to 1 or 2
4. Increase **Session Duration** and **Break Duration**
5. Check logs for any indications of rate limiting
   - Look for messages containing "RATE LIMIT REACHED" or "Rate limiting"
   - Note the timestamps to adjust your active hours accordingly

## Advanced Configuration

For power users who need finer control:

- **High Volume Handling**: Automatically adapts behavior when large numbers of downloads are queued
- **Rotate User-Agent**: Changes browser fingerprint between sessions
- **Vary Connection Parameters**: Adjusts HTTP headers to appear more natural

Remember that the goal is to make your download pattern appear as natural as possible. The most effective settings will depend on your typical usage patterns and the number of tracks you usually download. 