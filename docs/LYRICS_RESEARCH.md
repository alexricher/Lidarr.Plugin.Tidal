# Technical Design Document: Lyrics Integration for TrevTV's Tidal Download Plugin

## 1. Executive Summary

This document outlines a comprehensive implementation strategy for integrating reliable, free lyrics services into TrevTV's Tidal plugin for Lidarr. The solution addresses the need for a robust lyrics retrieval system with multiple fallback mechanisms to ensure continuous availability if primary services fail. All proposed services require only free registration, with no payment requirements.

---

## 2. Architecture Overview

### 2.1 System Architecture

The lyrics integration follows a multi-tiered, fault-tolerant architecture centered on service resilience. The system will implement a "waterfall" approach to lyrics retrieval, automatically cascading through available services until lyrics are successfully obtained.

![Architecture Diagram](https://mermaid.ink/img/pako:eNqNkk1Pg0AQhv_KZi-aaLPhq3jQg2lMTDx4MJw2w8JAt2V3KR-J8d-7UFppjYkXDrOT9513ZnYOIHMBkILK1bNUolCwE6X2G82VhLlKzVPMQxOrhVJoFhrbK8jN50ZpW9cX0MWuhK5vVWl5Z-cOJSRrmHK9rmRufh2iwtE-hAM4VkXTiL1G1y9kkxu0GcrlSm3hhTEWLVnj1n3QU7I_dg-eeiNr0-cUUAIjIQyCOU6CYOHPceTHMxJG08UUJ-EMB4sIR9MpCf0kjoKI-NF87s9JFEYL3yfRnPxt339nG3WDJeqmkQXu0O0eqzHMRKHzl46FLbIOBB50yNVVPgq1G8UuabX8lFLaLXb_Ry13FPdRh3mF1HSrYAfpC9SGHnrbjbmYylBnHbO9cYUq-Nwjt9UQKpHpV_9aXUNaDYdyXVOYQWoFaS Flow Architecture

The system follows this sequence to retrieve lyrics:

1. **Extract Song Metadata**: Parse artist, title, album information from Tidal API during download
2. **Clean Metadata**: Remove noise like "(feat. Artist)" or "[Radio Edit]" for optimal matching
3. **Primary Source Query**: Attempt Tidal's native lyrics API integration (if accessible)
4. **Tiered Service Queries**:
   - Tier 1: Musixmatch API (preferred for synchronized lyrics)
   - Tier 2: Genius API (strong database, but may require scraping)
   - Tier 3: NetEase Music API (excellent for Chinese content)
   - Tier 4: Self-hosted lrclib.net instance
5. **Caching Layer**: Store successful lyrics locally for rapid retrieval
6. **Output Generation**: Format and save as .lrc file alongside music files

### 2.3 Component Interaction Sequence

```
sequenceDiagram
    participant Lidarr
    participant TidalPlugin
    participant LyricsManager
    participant CacheManager
    participant PrimaryAPI as Tidal API
    participant SecondaryAPI as Musixmatch API
    participant TertiaryAPI as Genius API
    participant FallbackAPI as Local lrclib
    participant FileSystem

    Lidarr->>TidalPlugin: Request track download
    TidalPlugin->>PrimaryAPI: Fetch track metadata
    PrimaryAPI-->>TidalPlugin: Return metadata
    TidalPlugin->>LyricsManager: Request lyrics for track
    
    LyricsManager->>CacheManager: Check for cached lyrics
    alt Lyrics in cache
        CacheManager-->>LyricsManager: Return cached lyrics
    else Lyrics not in cache
        LyricsManager->>PrimaryAPI: Query Tidal native lyrics
        alt Tidal lyrics available
            PrimaryAPI-->>LyricsManager: Return lyrics
        else Tidal lyrics unavailable
            LyricsManager->>SecondaryAPI: Query Musixmatch
            alt Musixmatch success
                SecondaryAPI-->>LyricsManager: Return lyrics
            else Musixmatch failure
                LyricsManager->>TertiaryAPI: Query Genius
                alt Genius success
                    TertiaryAPI-->>LyricsManager: Return lyrics
                else Genius failure
                    LyricsManager->>FallbackAPI: Query local lrclib
                    FallbackAPI-->>LyricsManager: Return lyrics or failure
                end
            end
        end
        LyricsManager->>CacheManager: Cache successful lyrics
    end
    
    LyricsManager-->>TidalPlugin: Return lyrics or failure
    TidalPlugin->>FileSystem: Save audio file
    TidalPlugin->>FileSystem: Save .lrc file if lyrics found
    TidalPlugin-->>Lidarr: Report download completion
```

---

## 3. Detailed Service Integration

### 3.1 Tidal Native Lyrics API

#### 3.1.1 Service Specifications
- **Authentication**: Leverages existing Tidal authentication from the plugin
- **Endpoint**: `https://listen.tidal.com/v1/tracks/{trackId}/lyrics`
- **Rate Limits**: Subject to overall Tidal API limits, typically 5 requests/second
- **Response Format**: JSON with potential LRC timestamps

#### 3.1.2 Integration Implementation

```python
class TidalLyricsService:
    def __init__(self, session):
        self.session = session  # Reuse existing authenticated session
        self.base_url = "https://listen.tidal.com/v1"
        
    async def get_lyrics(self, track_id):
        """Retrieve lyrics directly from Tidal API if available"""
        url = f"{self.base_url}/tracks/{track_id}/lyrics"
        headers = {"Authorization": f"Bearer {self.session.access_token}"}
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(url, headers=headers) as response:
                    if response.status == 200:
                        data = await response.json()
                        if "text" in data or "subtitles" in data:
                            # Extract either plain text or synchronized lyrics
                            return self._parse_tidal_lyrics(data)
                    return None
        except Exception as e:
            logging.error(f"Tidal lyrics API error: {str(e)}")
            return None
            
    def _parse_tidal_lyrics(self, data):
        """Convert Tidal lyrics format to LRC format"""
        if "subtitles" in data:
            # Handle synchronized lyrics
            lrc_lines = []
            for line in data["subtitles"]:
                time_str = self._format_timestamp(line["time"])
                lrc_lines.append(f"[{time_str}]{line['text']}")
            return "\n".join(lrc_lines)
        elif "text" in data:
            # Handle plain text lyrics (no timestamps)
            return data["text"]
        return None
        
    def _format_timestamp(self, ms):
        """Convert milliseconds to LRC timestamp format"""
        seconds = ms / 1000
        minutes = int(seconds // 60)
        seconds = seconds % 60
        return f"{minutes:02d}:{seconds:05.2f}"
```

#### 3.1.3 Error Handling Strategy
- Implement retries with exponential backoff for 5xx errors
- Cache successful responses to minimize API calls
- Validate session token before requests, refreshing if expired

### 3.2 Musixmatch Integration

#### 3.2.1 Service Specifications
- **API Details**: Free developer tier with account registration
- **Daily Quota**: 2,000 API calls per day (free tier)
- **URL**: `https://api.musixmatch.com/ws/1.1/`
- **Features**: 
  - Synchronized timestamps
  - Multiple languages supported
  - High-quality lyrics database (~14 million songs)

#### 3.2.2 Authentication Process
```python
class MusixmatchService:
    def __init__(self, api_key):
        self.api_key = api_key
        self.base_url = "https://api.musixmatch.com/ws/1.1"
        self.limiter = RateLimiter(max_calls=2000, period=86400)  # 2000 calls per day
        
    @limiter
    async def get_track_id(self, artist, title):
        """Search for track ID by artist and title"""
        params = {
            "apikey": self.api_key,
            "q_artist": artist,
            "q_track": title,
            "page_size": 1,
            "page": 1,
            "s_track_rating": "desc"
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(
                    f"{self.base_url}/matcher.track.get", 
                    params=params
                ) as response:
                    if response.status == 200:
                        data = await response.json()
                        if data["message"]["header"]["status_code"] == 200:
                            body = data["message"]["body"]
                            if body and "track" in body:
                                return body["track"]["track_id"]
            return None
        except Exception as e:
            logging.error(f"Musixmatch track search error: {str(e)}")
            return None
    
    @limiter
    async def get_lyrics(self, track_id):
        """Get lyrics for a specific track ID"""
        params = {
            "apikey": self.api_key,
            "track_id": track_id
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(
                    f"{self.base_url}/track.lyrics.get", 
                    params=params
                ) as response:
                    if response.status == 200:
                        data = await response.json()
                        if data["message"]["header"]["status_code"] == 200:
                            body = data["message"]["body"]
                            if body and "lyrics" in body:
                                return body["lyrics"]["lyrics_body"]
            return None
        except Exception as e:
            logging.error(f"Musixmatch lyrics retrieval error: {str(e)}")
            return None
```

#### 3.2.3 Handling Partial Lyrics Limitation

The free Musixmatch API only returns partial lyrics. To overcome this limitation, implement either:

1. **LRC File Generation**: Convert partial lyrics to LRC format to provide some basic synchronized lyrics.

```python
def convert_to_lrc(lyrics, duration_ms):
    """Generate simple timestamps for partial lyrics"""
    if not lyrics:
        return None
        
    lines = [line for line in lyrics.split('\n') if line.strip()]
    # Remove commercial message typically found at the end
    lines = [line for line in lines if not line.startswith('...') and 'Musixmatch' not in line]
    
    if not lines:
        return None
        
    # Create evenly spaced timestamps
    line_count = len(lines)
    duration_sec = duration_ms / 1000
    interval = duration_sec / (line_count + 1)
    
    lrc_lines = []
    for i, line in enumerate(lines):
        timestamp = (i + 1) * interval
        minutes = int(timestamp // 60)
        seconds = timestamp % 60
        lrc_lines.append(f"[{minutes:02d}:{seconds:05.2f}]{line}")
        
    return "\n".join(lrc_lines)
```

2. **Lyrics Completion Strategy**: Implement a cooperative solution where users can manually complete lyrics if needed through the UI.

### 3.3 Genius Lyrics Integration

#### 3.3.1 Service Details
- **Authentication**: OAuth2 token (free with account)
- **Endpoints**: 
  - Search: `https://api.genius.com/search`
  - Song details: `https://api.genius.com/songs/{id}`
- **Rate Limits**: 1000 API calls/day (sufficient for most libraries)

#### 3.3.2 Lyrics Retrieval Process

```python
class GeniusService:
    def __init__(self, access_token):
        self.access_token = access_token
        self.base_url = "https://api.genius.com"
        self.headers = {"Authorization": f"Bearer {self.access_token}"}
        self.session = None
        self.limiter = RateLimiter(max_calls=950, period=86400)  # Conservative limit
        
    async def initialize(self):
        """Initialize aiohttp session"""
        if not self.session:
            self.session = aiohttp.ClientSession(headers=self.headers)
    
    async def close(self):
        """Close aiohttp session"""
        if self.session:
            await self.session.close()
            self.session = None
    
    @limiter
    async def search_song(self, artist, title):
        """Search for song on Genius"""
        await self.initialize()
        
        try:
            params = {"q": f"{artist} {title}"}
            async with self.session.get(f"{self.base_url}/search", params=params) as response:
                if response.status == 200:
                    data = await response.json()
                    for hit in data.get("response", {}).get("hits", []):
                        if hit["type"] == "song":
                            song = hit["result"]
                            if self._is_likely_match(song, artist, title):
                                return song["id"], song["url"]
            return None, None
        except Exception as e:
            logging.error(f"Genius search error: {str(e)}")
            return None, None
    
    async def get_lyrics(self, artist, title):
        """Get lyrics by scraping Genius webpage"""
        song_id, url = await self.search_song(artist, title)
        if not url:
            return None
            
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(url) as response:
                    if response.status == 200:
                        html = await response.text()
                        return self._extract_lyrics_from_html(html)
            return None
        except Exception as e:
            logging.error(f"Genius scraping error: {str(e)}")
            return None
    
    def _is_likely_match(self, song, artist, title):
        """Check if the song is likely a match for artist and title"""
        song_artist = song.get("primary_artist", {}).get("name", "").lower()
        song_title = song.get("title", "").lower()
        
        # Remove special characters and normalize
        artist = re.sub(r'[^\w\s]', '', artist.lower())
        title = re.sub(r'[^\w\s]', '', title.lower())
        song_artist = re.sub(r'[^\w\s]', '', song_artist)
        song_title = re.sub(r'[^\w\s]', '', song_title)
        
        # Check if artist matches or is contained
        artist_match = (
            song_artist == artist or
            artist in song_artist or
            song_artist in artist
        )
        
        # Check if title matches or is contained
        title_match = (
            song_title == title or
            title in song_title or
            song_title in title or
            self._fuzzy_match(song_title, title, threshold=85)
        )
        
        return artist_match and title_match
    
    def _fuzzy_match(self, str1, str2, threshold=85):
        """Fuzzy string matching using Levenshtein distance"""
        return fuzz.ratio(str1, str2) >= threshold
    
    def _extract_lyrics_from_html(self, html):
        """Extract lyrics from Genius HTML page"""
        soup = BeautifulSoup(html, 'html.parser')
        
        # Find lyrics container
        lyrics_div = soup.select_one('[data-lyrics-container="true"]')
        if not lyrics_div:
            return None
            
        # Extract and clean lyrics
        lyrics = []
        for element in lyrics_div:
            if isinstance(element, str):
                lyrics.append(element)
            elif element.name == 'br':
                lyrics.append('\n')
            elif element.get_text().strip():
                lyrics.append(element.get_text())
                
        return ''.join(lyrics).strip()
```

#### 3.3.3 Conversion to LRC Format

Since Genius provides only plain text lyrics, we need to approximate timestamps:

```python
class LRCGenerator:
    def generate_lrc_from_text(self, lyrics_text, duration_ms, offset_ms=0):
        """
        Generate LRC format from plain text lyrics with approximate timestamps
        
        Args:
            lyrics_text (str): Plain text lyrics with line breaks
            duration_ms (int): Song duration in milliseconds
            offset_ms (int): Offset to apply to all timestamps (ms)
            
        Returns:
            str: LRC formatted lyrics
        """
        if not lyrics_text or not duration_ms:
            return None
            
        lines = [line.strip() for line in lyrics_text.split('\n') if line.strip()]
        if not lines:
            return None
            
        # Generate timestamps with better distribution
        timestamps = self._distribute_timestamps(len(lines), duration_ms, offset_ms)
        
        # Create LRC lines
        lrc_lines = []
        for i, line in enumerate(lines):
            time_str = self._format_timestamp(timestamps[i])
            lrc_lines.append(f"[{time_str}]{line}")
            
        # Add metadata
        metadata = [
            "[ar:Unknown]",  # Can be replaced with actual artist
            "[al:Unknown]",  # Can be replaced with actual album
            "[ti:Unknown]",  # Can be replaced with actual title
            "[by:TidalPlugin]",
            f"[length:{self._format_timestamp(duration_ms)}]"
        ]
        
        return "\n".join(metadata + lrc_lines)
    
    def _distribute_timestamps(self, line_count, duration_ms, offset_ms=0):
        """
        Distribute timestamps using a more natural progression
        
        This uses a slightly frontloaded distribution since most songs
        have more lyrics in the beginning and middle than at the end
        """
        if line_count  docker-compose.yml  self.ttl_seconds:
                    return None
                
                # Check if file exists
                if not os.path.exists(file_path):
                    return None
                
                # Read lyrics from file
                with open(file_path, 'r', encoding='utf-8') as f:
                    return f.read()
                    
        except Exception as e:
            logging.error(f"Cache read error: {str(e)}")
            return None
    
    async def set(self, artist, title, lyrics, service, album=None):
        """Save lyrics to cache"""
        if not lyrics:
            return False
            
        try:
            key = self._generate_cache_key(artist, title, album)
            file_path = os.path.join(self.cache_dir, f"{key}.lrc")
            
            # Write lyrics to file
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(lyrics)
            
            # Update cache index
            with sqlite3.connect(self.db_path) as conn:
                cursor = conn.cursor()
                cursor.execute('''
                    INSERT OR REPLACE INTO lyrics_cache 
                    (artist, title, album, file_path, service, timestamp)
                    VALUES (?, ?, ?, ?, ?, ?)
                ''', (
                    artist.lower(), 
                    title.lower(), 
                    album.lower() if album else None,
                    file_path,
                    service,
                    int(time.time())
                ))
                conn.commit()
                
            return True
            
        except Exception as e:
            logging.error(f"Cache write error: {str(e)}")
            return False
    
    async def clear_expired(self):
        """Clear expired cache entries"""
        try:
            with sqlite3.connect(self.db_path) as conn:
                cursor = conn.cursor()
                expiry_time = int(time.time()) - self.ttl_seconds
                
                # Get expired entries
                cursor.execute('''
                    SELECT file_path FROM lyrics_cache
                    WHERE timestamp 
            Lyrics Download Settings
            
            
                Tidal Native Lyrics
                
                    
                        
                        Use Tidal's native lyrics (when available)
                    
                
            
            
            
                Musixmatch
                
                    
                        
                        Enable Musixmatch
                    
                
                
                    API Key:
                    
                    Get API Key
                
            
            
            
                Genius
                
                    
                        
                        Enable Genius
                    
                
                
                    Access Token:
                    
                    Get Token
                
            
            
            
                NetEase Music
                
                    
                        
                        Enable NetEase Music API
                    
                
                
                    API URL:
                    
                
            
            
            
                LRCLib
                
                    
                        
                        Enable LRCLib
                    
                
                
                    API URL:
                    
                
            
            
            
                Cache Settings
                
                    Cache Duration (days):
                    
                
                Clear Cache
            
            
            
                Save Settings
            
        
        """
    
    def handle_settings_save(self, form_data):
        """Process settings form submission"""
        # Update configuration
        self.config['use_tidal_lyrics'] = form_data.get('use_tidal_lyrics') == 'true'
        self.config['use_musixmatch'] = form_data.get('use_musixmatch') == 'true'
        self.config['musixmatch_api_key'] = form_data.get('musixmatch_api_key', '')
        self.config['use_genius'] = form_data.get('use_genius') == 'true'
        self.config['genius_token'] = form_data.get('genius_token', '')
        self.config['use_netease'] = form_data.get('use_netease') == 'true'
        self.config['netease_api_url'] = form_data.get('netease_api_url', 'http://localhost:3000')
        self.config['use_lrclib'] = form_data.get('use_lrclib') == 'true'
        self.config['lrclib_url'] = form_data.get('lrclib_url', 'http://localhost:5000')
        self.config['cache_ttl'] = int(form_data.get('cache_ttl', 30))
        
        # Save configuration
        self._save_config()
        
        return {'success': True, 'message': 'Lyrics settings saved successfully'}
    
    def _save_config(self):
        """Save configuration to file"""
        config_path = os.path.join(os.path.dirname(__file__), 'config', 'lyrics_config.json')
        os.makedirs(os.path.dirname(config_path), exist_ok=True)
        
        with open(config_path, 'w', encoding='utf-8') as f:
            json.dump(self.config, f, indent=2)
```

### 5.2 Status Dashboard Components

```python
class LyricsDashboardComponent:
    def __init__(self, lyrics_manager):
        self.lyrics_manager = lyrics_manager
        
    async def render_statistics(self):
        """Render statistics panel for dashboard"""
        stats = await self.lyrics_manager.get_stats()
        
        cache_total = stats['cache']['total_entries']
        cache_size_mb = stats['cache']['size_bytes'] / (1024 * 1024)
        hit_rate = stats['hit_rate']
        
        service_stats = []
        for name, count in stats['service_hits'].items():
            service_stats.append({
                'name': name.capitalize(),
                'count': count,
                'percentage': (count / max(1, stats['requests'])) * 100
            })
        
        return f"""
        
            Lyrics Statistics
            
            
                
                    {stats['requests']}
                    Total Requests
                
                
                    {hit_rate:.1f}%
                    Success Rate
                
                
                    {stats['cache_hits']}
                    Cache Hits
                
                
                    {cache_total}
                    Cached Entries
                
                
                    {cache_size_mb:.2f} MB
                    Cache Size
                
            
            
            Service Distribution
            
                
                    
                        
                            Service
                            Hits
                            Percentage
                        
                    
                    
                        {''.join(f'{s["name"]}{s["count"]}{s["percentage"]:.1f}%' for s in service_stats)}
                    
                
            
        
        """
```

---

## 6. Integration with TrevTV's Tidal Plugin

### 6.1 Plugin Hook Registration

```python
def register_plugin_hooks(tidal_plugin):
    """Register lyrics functionality with the Tidal plugin"""
    # Load configuration
    config_path = os.path.join(os.path.dirname(__file__), 'config', 'lyrics_config.json')
    try:
        with open(config_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
    except:
        config = {}
    
    # Initialize components
    lyrics_manager = LyricsManager(config)
    settings_component = LyricsSettingsComponent(config)
    dashboard_component = LyricsDashboardComponent(lyrics_manager)
    
    # Register hooks for download process
    tidal_plugin.register_post_download_hook('lyrics_download', _download_lyrics_hook(lyrics_manager))
    
    # Register settings page
    tidal_plugin.register_settings_page('lyrics', 'Lyrics', settings_component.render_settings_page)
    tidal_plugin.register_settings_handler('lyrics', settings_component.handle_settings_save)
    
    # Register dashboard component
    tidal_plugin.register_dashboard_component('lyrics_stats', dashboard_component.render_statistics)
    
    # Register API endpoints for AJAX functionality
    tidal_plugin.register_api_endpoint('lyrics/stats', _get_lyrics_stats(lyrics_manager))
    tidal_plugin.register_api_endpoint('lyrics/clear-cache', _clear_lyrics_cache(lyrics_manager))

def _download_lyrics_hook(lyrics_manager):
    """Create hook function for post-download processing"""
    async def hook(download_result):
        """Process track after download to add lyrics"""
        if download_result.get('success') and download_result.get('file_path'):
            track_metadata = {
                'artist': download_result.get('artist', ''),
                'title': download_result.get('title', ''),
                'album': download_result.get('album', ''),
                'track_id': download_result.get('id'),
                'duration_ms': download_result.get('duration', 0) * 1000  # Convert to ms
            }
            
            lyrics, service = await lyrics_manager.get_lyrics(track_metadata)
            if lyrics:
                await lyrics_manager.save_lyrics_file(download_result['file_path'], lyrics)
                return {
                    **download_result,
                    'lyrics_added': True,
                    'lyrics_source': service
                }
            
            return {
                **download_result,
                'lyrics_added': False
            }
        
        return download_result
    
    return hook

def _get_lyrics_stats(lyrics_manager):
    """Create API endpoint handler for statistics"""
    async def handler(request):
        """Return lyrics statistics as JSON"""
        stats = await lyrics_manager.get_stats()
        return {'status': 'success', 'data': stats}
    
    return handler

def _clear_lyrics_cache(lyrics_manager):
    """Create API endpoint handler for cache clearing"""
    async def handler(request):
        """Clear the lyrics cache"""
        deleted_count = await lyrics_manager.cache.clear_expired()
        return {
            'status': 'success', 
            'message': f'Cleared {deleted_count} expired cache entries'
        }
    
    return handler
```

### 6.2 Plugin Installation Strategy

```python
def install_lyrics_module():
    """Install the lyrics module into the Tidal plugin"""
    plugin_dir = os.path.dirname(os.path.abspath(__file__))
    tidal_plugin_dir = os.path.join(plugin_dir, '..', '..', 'TidalPlugin')
    
    # Create necessary directories
    lyrics_dir = os.path.join(tidal_plugin_dir, 'lyrics')
    os.makedirs(lyrics_dir, exist_ok=True)
    os.makedirs(os.path.join(lyrics_dir, 'config'), exist_ok=True)
    os.makedirs(os.path.join(lyrics_dir, 'cache'), exist_ok=True)
    
    # Copy module files
    shutil.copy('lyrics_manager.py', os.path.join(lyrics_dir, 'lyrics_manager.py'))
    shutil.copy('lyrics_services.py', os.path.join(lyrics_dir, 'lyrics_services.py'))
    shutil.copy('lyrics_ui.py', os.path.join(lyrics_dir, 'lyrics_ui.py'))
    shutil.copy('install.py', os.path.join(lyrics_dir, 'install.py'))
    
    # Create default configuration if not exists
    config_path = os.path.join(lyrics_dir, 'config', 'lyrics_config.json')
    if not os.path.exists(config_path):
        default_config = {
            'use_tidal_lyrics': True,
            'use_musixmatch': True,
            'musixmatch_api_key': '',
            'use_genius': True,
            'genius_token': '',
            'use_netease': False,
            'netease_api_url': 'http://localhost:3000',
            'use_lrclib': True,
            'lrclib_url': 'http://localhost:5000',
            'cache_ttl': 30
        }
        
        with open(config_path, 'w', encoding='utf-8') as f:
            json.dump(default_config, f, indent=2)
    
    # Register plugin with Tidal plugin
    plugin_init_path = os.path.join(tidal_plugin_dir, 'plugins.py')
    
    with open(plugin_init_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    if 'import lyrics_manager' not in content:
        # Add import
        import_line = 'from lyrics.lyrics_manager import register_plugin_hooks as register_lyrics_hooks'
        content = content.replace('# Plugin imports', f'# Plugin imports\n{import_line}')
        
        # Add registration
        register_line = '    register_lyrics_hooks(plugin)'
        content = content.replace('    # Register plugins', f'    # Register plugins\n{register_line}')
        
        # Write changes
        with open(plugin_init_path, 'w', encoding='utf-8') as f:
            f.write(content)
    
    print("Lyrics module installed successfully!")
```

---

## 7. Performance Optimization

### 7.1 Asynchronous Processing

```python
class AsyncLyricsProcessor:
    def __init__(self, lyrics_manager, max_concurrent=5):
        self.lyrics_manager = lyrics_manager
        self.processing_queue = asyncio.Queue()
        self.max_concurrent = max_concurrent
        self.running = False
        self.workers = []
    
    async def start(self):
        """Start the background processing of lyrics requests"""
        if self.running:
            return
            
        self.running = True
        
        # Start worker tasks
        for _ in range(self.max_concurrent):
            worker = asyncio.create_task(self._worker())
            self.workers.append(worker)
    
    async def stop(self):
        """Stop the background processing"""
        if not self.running:
            return
            
        self.running = False
        
        # Add termination signals
        for _ in range(len(self.workers)):
            await self.processing_queue.put(None)
        
        # Wait for all workers to complete
        if self.workers:
            await asyncio.gather(*self.workers)
            self.workers = []
    
    async def queue_track(self, track_metadata, file_path):
        """Queue a track for lyrics processing"""
        await self.processing_queue.put({
            'metadata': track_metadata,
            'file_path': file_path
        })
    
    async def _worker(self):
        """Worker process that handles lyrics retrieval"""
        while self.running:
            try:
                # Get next item from queue
                item = await self.processing_queue.get()
                
                # Check for termination signal
                if item is None:
                    break
                
                # Process the track
                metadata = item['metadata']
                file_path = item['file_path']
                
                # Get lyrics
                lyrics, service = await self.lyrics_manager.get_lyrics(metadata)
                
                # Save if found
                if lyrics:
                    await self.lyrics_manager.save_lyrics_file(file_path, lyrics)
                    logging.info(f"Lyrics added for {metadata.get('artist')} - {metadata.get('title')} from {service}")
                else:
                    logging.info(f"No lyrics found for {metadata.get('artist')} - {metadata.get('title')}")
                
            except Exception as e:
                logging.error(f"Error in lyrics worker: {str(e)}")
            finally:
                # Mark task as done
                self.processing_queue.task_done()
```

### 7.2 Batch Processing for Multiple Tracks

```python
class BatchLyricsProcessor:
    def __init__(self, lyrics_manager):
        self.lyrics_manager = lyrics_manager
    
    async def process_batch(self, tracks, max_concurrent=10):
        """
        Process a batch of tracks to add lyrics
        
        Args:
            tracks: List of dictionaries with keys:
                - metadata: Track metadata
                - file_path: Path to the audio file
            max_concurrent: Maximum number of concurrent tasks
            
        Returns:
            dict: Results with success/failure counts
        """
        # Create semaphore to limit concurrency
        semaphore = asyncio.Semaphore(max_concurrent)
        
        async def process_track(track):
            """Process a single track with concurrency limit"""
            async with semaphore:
                metadata = track['metadata']
                file_path = track['file_path']
                
                lyrics, service = await self.lyrics_manager.get_lyrics(metadata)
                
                if lyrics:
                    success = await self.lyrics_manager.save_lyrics_file(file_path, lyrics)
                    return {
                        'track_id': metadata.get('track_id'),
                        'success': success,
                        'service': service
                    }
                else:
                    return {
                        'track_id': metadata.get('track_id'),
                        'success': False,
                        'service': None
                    }
        
        # Create tasks for all tracks
        tasks = [process_track(track) for track in tracks]
        
        # Process all tasks and gather results
        results = await asyncio.gather(*tasks)
        
        # Compile statistics
        success_count = sum(1 for r in results if r['success'])
        service_counts = {}
        
        for r in results:
            if r['success'] and r['service']:
                service_counts[r['service']] = service_counts.get(r['service'], 0) + 1
        
        return {
            'total': len(tracks),
            'success': success_count,
            'failed': len(tracks) - success_count,
            'by_service': service_counts
        }
```

### 7.3 Connection Pooling

```python
class ConnectionManager:
    def __init__(self, timeout=10, pool_size=100):
        self.timeout = timeout
        self.pool_size = pool_size
        self.connector = None
        self.session = None
    
    async def initialize(self):
        """Initialize connection pool"""
        if not self.connector:
            self.connector = aiohttp.TCPConnector(
                limit=self.pool_size,
                ttl_dns_cache=300,  # 5 minutes DNS cache
                use_dns_cache=True
            )
        
        if not self.session:
            self.session = aiohttp.ClientSession(
                connector=self.connector,
                timeout=aiohttp.ClientTimeout(total=self.timeout)
            )
    
    async def close(self):
        """Close connection pool"""
        if self.session:
            await self.session.close()
            self.session = None
        
        if self.connector:
            await self.connector.close()
            self.connector = None
    
    async def get_session(self):
        """Get the shared client session"""
        if not self.session:
            await self.initialize()
        return self.session
```

---

## 8. Security Considerations

### 8.1 API Key Encryption

```python
class CredentialManager:
    def __init__(self, config_dir):
        self.config_dir = config_dir
        self.key_file = os.path.join(config_dir, 'encryption.key')
        self._ensure_key_exists()
    
    def _ensure_key_exists(self):
        """Ensure encryption key exists or create it"""
        os.makedirs(self.config_dir, exist_ok=True)
        
        if not os.path.exists(self.key_file):
            # Generate new key
            key = Fernet.generate_key()
            with open(self.key_file, 'wb') as f:
                f.write(key)
    
    def _get_key(self):
        """Get encryption key"""
        with open(self.key_file, 'rb') as f:
            return f.read()
    
    def encrypt(self, plain_text):
        """Encrypt sensitive data"""
        if not plain_text:
            return ''
            
        key = self._get_key()
        f = Fernet(key)
        return f.encrypt(plain_text.encode()).decode()
    
    def decrypt(self, encrypted_text):
        """Decrypt sensitive data"""
        if not encrypted_text:
            return ''
            
        try:
            key = self._get_key()
            f = Fernet(key)
            return f.decrypt(encrypted_text.encode()).decode()
        except:
            return ''
    
    def secure_config(self, config):
        """Create a secure version of configuration with encrypted API keys"""
        secure_config = config.copy()
        
        # Encrypt sensitive fields
        if 'musixmatch_api_key' in secure_config and secure_config['musixmatch_api_key']:
            secure_config['musixmatch_api_key'] = self.encrypt(secure_config['musixmatch_api_key'])
        
        if 'genius_token' in secure_config and secure_config['genius_token']:
            secure_config['genius_token'] = self.encrypt(secure_config['genius_token'])
        
        return secure_config
    
    def decrypt_config(self, secure_config):
        """Decrypt the encrypted fields in configuration"""
        config = secure_config.copy()
        
        # Decrypt sensitive fields
        if 'musixmatch_api_key' in config and config['musixmatch_api_key']:
            config['musixmatch_api_key'] = self.decrypt(config['musixmatch_api_key'])
        
        if 'genius_token' in config and config['genius_token']:
            config['genius_token'] = self.decrypt(config['genius_token'])
        
        return config
```

### 8.2 Rate Limiting Protection

```python
class RateLimiter:
    """
    Decorator class for rate limiting API requests
    
    Args:
        max_calls: Maximum number of calls allowed in the period
        period: Time period in seconds
    """
    def __init__(self, max_calls, period):
        self.max_calls = max_calls
        self.period = period
        self.calls = []
        self.lock = asyncio.Lock()
    
    def __call__(self, func):
        """Wrap the function with rate limiting"""
        @functools.wraps(func)
        async def wrapper(*args, **kwargs):
            # Remove old calls
            now = time.time()
            
            async with self.lock:
                self.calls = [t for t in self.calls if now - t = self.max_calls:
                    # Calculate time to wait
                    oldest_call = min(self.calls)
                    wait_time = self.period - (now - oldest_call)
                    
                    if wait_time > 0:
                        logging.warning(f"Rate limit reached. Waiting {wait_time:.2f}s")
                        await asyncio.sleep(wait_time)
                
                # Add this call
                self.calls.append(time.time())
            
            # Call the function
            return await func(*args, **kwargs)
        
        return wrapper
```

---

## 9. Testing Strategy

### 9.1 Unit Testing Framework

```python
import unittest
from unittest.mock import AsyncMock, MagicMock, patch

class TestLyricsServices(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        """Set up test fixtures"""
        # Mock configuration
        self.config = {
            'musixmatch_api_key': 'test_key',
            'genius_token': 'test_token',
            'use_tidal_lyrics': True,
            'use_musixmatch': True,
            'use_genius': True,
            'use_lrclib': True
        }
        
        # Create services with mocked API calls
        self.tidal_service = TidalLyricsService(MagicMock())
        self.tidal_service.get_lyrics = AsyncMock(return_value="[00:01.00]Test lyrics")
        
        self.musixmatch_service = MusixmatchService('test_key')
        self.musixmatch_service.get_track_id = AsyncMock(return_value="12345")
        self.musixmatch_service.get_lyrics = AsyncMock(return_value="Test musixmatch lyrics")
        
        self.genius_service = GeniusService('test_token')
        self.genius_service.get_lyrics = AsyncMock(return_value="Test genius lyrics")
        
        self.lrclib_service = LRCLibService()
        self.lrclib_service.get_lyrics = AsyncMock(return_value="[00:00.00]Test lrclib lyrics")
        
        # Create lyrics manager with mocked services
        self.lyrics_manager = LyricsManager(self.config)
        self.lyrics_manager.services = [
            {'name': 'tidal', 'service': self.tidal_service, 'enabled': True, 'priority': 1},
            {'name': 'musixmatch', 'service': self.musixmatch_service, 'enabled': True, 'priority': 2},
            {'name': 'genius', 'service': self.genius_service, 'enabled': True, 'priority': 3},
            {'name': 'lrclib', 'service': self.lrclib_service, 'enabled': True, 'priority': 4}
        ]
        
        # Mock cache
        self.lyrics_manager.cache.get = AsyncMock(return_value=None)
        self.lyrics_manager.cache.set = AsyncMock(return_value=True)
    
    async def test_get_lyrics_tidal(self):
        """Test getting lyrics from Tidal service"""
        track_metadata = {
            'artist': 'Test Artist',
            'title': 'Test Title',
            'track_id': '12345'
        }
        
        lyrics, service = await self.lyrics_manager.get_lyrics(track_metadata)
        
        self.assertEqual(service, 'tidal')
        self.assertEqual(lyrics, "[00:01.00]Test lyrics")
        self.tidal_service.get_lyrics.assert_called_once_with('12345')
    
    async def test_get_lyrics_musixmatch_fallback(self):
        """Test fallback to Musixmatch when Tidal fails"""
        track_metadata = {
            'artist': 'Test Artist',
            'title': 'Test Title',
            'track_id': '12345'
        }
        
        # Make Tidal service fail
        self.tidal_service.get_lyrics = AsyncMock(return_value=None)
        
        lyrics, service = await self.lyrics_manager.get_lyrics(track_metadata)
        
        self.assertEqual(service, 'musixmatch')
        self.assertEqual(lyrics, "Test musixmatch lyrics")
        self.musixmatch_service.get_track_id.assert_called_once()
        self.musixmatch_service.get_lyrics.assert_called_once_with('12345')
    
    async def test_get_lyrics_from_cache(self):
        """Test retrieving lyrics from cache"""
        track_metadata = {
            'artist': 'Test Artist',
            'title': 'Test Title'
        }
        
        # Set up cache hit
        self.lyrics_manager.cache.get = AsyncMock(return_value="[00:05.00]Cached lyrics")
        
        lyrics, service = await self.lyrics_manager.get_lyrics(track_metadata)
        
        self.assertEqual(service, 'cache')
        self.assertEqual(lyrics, "[00:05.00]Cached lyrics")
        self.tidal_service.get_lyrics.assert_not_called()
```

### 9.2 Integration Testing

```python
class TestLyricsIntegration(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        """Set up test fixtures for integration tests"""
        # Create a temporary directory for files
        self.temp_dir = tempfile.mkdtemp()
        
        # Create actual service instances with real or mock APIs
        # For testing we'll use actual APIs with reduced rate limits
        self.config = {
            'use_tidal_lyrics': False,  # We don't have a real Tidal session for tests
            'use_musixmatch': True,
            'musixmatch_api_key': os.environ.get('MUSIXMATCH_API_KEY', ''),
            'use_genius': True,
            'genius_token': os.environ.get('GENIUS_API_KEY', ''),
            'use_lrclib': True,
            'cache_dir': os.path.join(self.temp_dir, 'cache')
        }
        
        # Create actual instance of lyrics manager
        self.lyrics_manager = LyricsManager(self.config)
    
    async def asyncTearDown(self):
        """Clean up after tests"""
        # Remove temp directory
        shutil.rmtree(self.temp_dir)
    
    async def test_end_to_end_popular_song(self):
        """Test retrieving lyrics for a well-known song"""
        track_metadata = {
            'artist': 'Queen',
            'title': 'Bohemian Rhapsody',
            'album': 'A Night at the Opera',
            'duration_ms': 354000
        }
        
        lyrics, service = await self.lyrics_manager.get_lyrics(track_metadata)
        
        # Check that we got lyrics from one of our services
        self.assertIsNotNone(lyrics)
        self.assertIn(service, ['musixmatch', 'genius', 'lrclib'])
        
        # Check that lyrics contain some expected content
        self.assertIn("Galileo", lyrics)
    
    async def test_save_lyrics_file(self):
        """Test saving lyrics to a file"""
        # Create a temp audio file
        audio_path = os.path.join(self.temp_dir, 'test_song.mp3')
        with open(audio_path, 'wb') as f:
            f.write(b'dummy mp3 content')
        
        # Sample lyrics
        test_lyrics = "[00:01.00]This is a test lyric\n[00:05.00]Second line of lyrics"
        
        # Save lyrics
        result = await self.lyrics_manager.save_lyrics_file(audio_path, test_lyrics)
        
        # Check results
        self.assertTrue(result)
        
        # Check that file exists
        lrc_path = os.path.join(self.temp_dir, 'test_song.lrc')
        self.assertTrue(os.path.exists(lrc_path))
        
        # Check file contents
        with open(lrc_path, 'r', encoding='utf-8') as f:
            content = f.read()
            self.assertEqual(content, test_lyrics)
```

### 9.3 Performance Testing

```python
class TestLyricsPerformance(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        """Set up test fixtures for performance tests"""
        # Create a temporary directory for cache
        self.temp_dir = tempfile.mkdtemp()
        
        # Configuration with mock services for performance testing
        self.config = {
            'use_tidal_lyrics': True,
            'use_musixmatch': True,
            'use_genius': True,
            'use_lrclib': True,
            'cache_dir': os.path.join(self.temp_dir, 'cache')
        }
        
        # Create mock services that simulate different response times
        self.tidal_service = AsyncMock()
        self.tidal_service.get_lyrics.side_effect = self._delayed_response("Tidal lyrics", 0.2)
        
        self.musixmatch_service = AsyncMock()
        self.musixmatch_service.get_track_id.side_effect = self._delayed_response("12345", 0.3)
        self.musixmatch_service.get_lyrics.side_effect = self._delayed_response("Musixmatch lyrics", 0.5)
        
        self.genius_service = AsyncMock()
        self.genius_service.get_lyrics.side_effect = self._delayed_response("Genius lyrics", 0.8)
        
        self.lrclib_service = AsyncMock()
        self.lrclib_service.get_lyrics.side_effect = self._delayed_response("LRCLib lyrics", 0.4)
        
        # Create lyrics manager with mock services
        self.lyrics_manager = LyricsManager(self.config)
        self.lyrics_manager.services = [
            {'name': 'tidal', 'service': self.tidal_service, 'enabled': True, 'priority': 1},
            {'name': 'musixmatch', 'service': self.musixmatch_service, 'enabled': True, 'priority': 2},
            {'name': 'genius', 'service': self.genius_service, 'enabled': True, 'priority': 3},
            {'name': 'lrclib', 'service': self.lrclib_service, 'enabled': True, 'priority': 4}
        ]
    
    def _delayed_response(self, result, delay):
        """Create an async function that returns after a delay"""
        async def delayed_func(*args, **kwargs):
            await asyncio.sleep(delay)
            return result
        return delayed_func
    
    async def asyncTearDown(self):
        """Clean up after tests"""
        shutil.rmtree(self.temp_dir)
    
    async def test_sequential_performance(self):
        """Test performance of sequential lyrics retrieval"""
        # Generate 20 test tracks
        test_tracks = []
        for i in range(20):
            test_tracks.append({
                'artist': f'Artist {i}',
                'title': f'Title {i}',
                'track_id': str(i)
            })
        
        # Measure time for sequential processing
        start_time = time.time()
        
        for track in test_tracks:
            await self.lyrics_manager.get_lyrics(track)
        
        end_time = time.time()
        sequential_time = end_time - start_time
        
        print(f"Sequential processing time: {sequential_time:.2f}s")
    
    async def test_parallel_performance(self):
        """Test performance of parallel lyrics retrieval"""
        # Generate 20 test tracks
        test_tracks = []
        for i in range(20):
            test_tracks.append({
                'artist': f'Artist {i}',
                'title': f'Title {i}',
                'track_id': str(i)
            })
        
        # Create batch processor
        batch_processor = BatchLyricsProcessor(self.lyrics_manager)
        
        # Measure time for parallel processing
        start_time = time.time()
        
        # Convert tracks to format expected by batch processor
        batch_tracks = [{'metadata': track, 'file_path': f'/tmp/track{i}.mp3'} 
                       for i, track in enumerate(test_tracks)]
        
        await batch_processor.process_batch(batch_tracks, max_concurrent=10)
        
        end_time = time.time()
        parallel_time = end_time - start_time
        
        print(f"Parallel processing time: {parallel_time:.2f}s")
```

---

## 10. Maintenance and Future Enhancements

### 10.1 Logging Strategy

```python
def configure_logging(log_dir=None, level=logging.INFO):
    """Configure logging for lyrics module"""
    if log_dir is None:
        log_dir = os.path.join(os.path.dirname(__file__), 'logs')
    
    os.makedirs(log_dir, exist_ok=True)
    
    # Create a rotating file handler
    log_file = os.path.join(log_dir, 'lyrics.log')
    file_handler = RotatingFileHandler(
        log_file, 
        maxBytes=5 * 1024 * 1024,  # 5 MB
        backupCount=3
    )
    
    # Create console handler
    console_handler = logging.StreamHandler()
    
    # Create formatter
    formatter = logging.Formatter(
        '%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )
    file_handler.setFormatter(formatter)
    console_handler.setFormatter(formatter)
    
    # Configure root logger
    logger = logging.getLogger('lyrics')
    logger.setLevel(level)
    logger.addHandler(file_handler)
    logger.addHandler(console_handler)
    
    return logger

def log_api_request(service, endpoint, params=None, response_code=None, error=None):
    """Log API request for monitoring"""
    logger = logging.getLogger('lyrics.api')
    
    log_data = {
        'timestamp': datetime.datetime.now().isoformat(),
        'service': service,
        'endpoint': endpoint
    }
    
    if params:
        # Remove sensitive information
        safe_params = params.copy()
        if 'apikey' in safe_params:
            safe_params['apikey'] = '***'
        if 'access_token' in safe_params:
            safe_params['access_token'] = '***'
        log_data['params'] = safe_params
    
    if response_code:
        log_data['response_code'] = response_code
    
    if error:
        log_data['error'] = str(error)
        logger.error(json.dumps(log_data))
    else:
        logger.info(json.dumps(log_data))
```

### 10.2 Future Enhancements

#### 10.2.1 Community Lyrics Contribution

```python
class CommunityLyricsService:
    def __init__(self, api_url="http://localhost:8000"):
        self.api_url = api_url
        
    async def submit_lyrics(self, artist, title, lyrics, user_id=None):
        """Submit community-contributed lyrics"""
        data = {
            'artist': artist,
            'title': title,
            'lyrics': lyrics,
            'contributor': user_id or 'anonymous'
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(
                    f"{self.api_url}/api/lyrics/submit", 
                    json=data
                ) as response:
                    if response.status == 200:
                        return await response.json()
            return None
        except Exception as e:
            logging.error(f"Community lyrics submission error: {str(e)}")
            return None
    
    async def get_community_lyrics(self, artist, title):
        """Get community-contributed lyrics"""
        params = {
            'artist': artist,
            'title': title
        }
        
        try:
            async with aiohttp.ClientSession() as session:
                async with session.get(
                    f"{self.api_url}/api/lyrics/get", 
                    params=params
                ) as response:
                    if response.status == 200:
                        return await response.json()
            return None
        except Exception as e:
            logging.error(f"Community lyrics retrieval error: {str(e)}")
            return None
```

#### 10.2.2 LRC Editor UI Component

```python
class LRCEditorComponent:
    def __init__(self):
        pass
        
    def render_editor(self, artist, title, lyrics=None):
        """Render LRC editor UI component"""
        return f"""
        
            LRC Editor - {artist} - {title}
            
            
                
                    
                    Your browser does not support the audio element.
                
                
                
                    Play
                    Pause
                    Stop
                    00:00.00 / 00:00.00
                
            
            
            
                
                    {lyrics or ''}
                
                
                
                    Add Timestamp
                    Auto Timestamp
                    Clear Timestamps
                
                
                
                    Save LRC
                    Preview
                    Export
                
            
            
            
                Preview
                
            
        
        
        
        
            // Initialize editor with track data
            new LRCEditor({
                artist: '{artist}',
                title: '{title}',
                audioUrl: '/api/stream?artist={artist}&title={title}',
                saveUrl: '/api/lyrics/save'
            });
        
        """
```

# Technical Design Document: Lyrics Integration for TrevTV's Tidal Plugin (Continued)

## 10. Maintenance and Future Enhancements (Continued)

### 10.2.3 Multi-language Support (Continued)

```python
    def _extract_text_from_lrc(self, lrc_text):
        """Extract plain text from LRC format"""
        if not lrc_text:
            return ""
            
        # Extract text after timestamps
        timestamp_pattern = re.compile(r'^\[\d{2}:\d{2}\.\d{2}\](.*?)$')
        
        lines = []
        for line in lrc_text.split('\n'):
            match = timestamp_pattern.match(line.strip())
            if match:
                text = match.group(1).strip()
                if text:
                    lines.append(text)
        
        return '\n'.join(lines)
    
    def _rebuild_lrc_with_translation(self, original_lrc, translated_text):
        """Rebuild LRC format using original timestamps but translated text"""
        if not original_lrc or not translated_text:
            return original_lrc
            
        # Split translated text into lines
        translated_lines = translated_text.split('\n')
        
        # Extract timestamps and text from original LRC
        timestamp_pattern = re.compile(r'^(\[\d{2}:\d{2}\.\d{2}\])(.*?)$')
        
        original_parts = []
        for line in original_lrc.split('\n'):
            match = timestamp_pattern.match(line.strip())
            if match:
                timestamp = match.group(1)
                text = match.group(2).strip()
                if text:
                    original_parts.append((timestamp, text))
        
        # If line counts don't match, cannot reliably rebuild
        if len(original_parts) != len(translated_lines):
            logging.warning("Translation line count mismatch, cannot rebuild LRC")
            return original_lrc
        
        # Rebuild LRC with translated text
        result_lines = []
        for i, (timestamp, _) in enumerate(original_parts):
            if i  /dev/null; then
    echo "Docker is required but not installed. Please install Docker first."
    exit 1
fi

# Check if Docker Compose is installed
if ! command -v docker-compose &> /dev/null; then
    echo "Docker Compose is required but not installed. Please install Docker Compose first."
    exit 1
fi

# Create directories
mkdir -p ./lyrics-services/data/{lrclib,netease,community}

# Download docker-compose.yml
curl -o ./lyrics-services/docker-compose.yml https://raw.githubusercontent.com/yourusername/tidal-lyrics/main/docker-compose.yml

# Download Python integration module
mkdir -p ./tidal-lyrics-module
curl -o ./tidal-lyrics-module/lyrics_manager.py https://raw.githubusercontent.com/yourusername/tidal-lyrics/main/lyrics_manager.py
curl -o ./tidal-lyrics-module/lyrics_services.py https://raw.githubusercontent.com/yourusername/tidal-lyrics/main/lyrics_services.py
curl -o ./tidal-lyrics-module/install.py https://raw.githubusercontent.com/yourusername/tidal-lyrics/main/install.py

# Start services
cd ./lyrics-services
docker-compose up -d

# Install Python requirements
pip install aiohttp beautifulsoup4 fuzzywuzzy python-Levenshtein tenacity cryptography

# Run installer
cd ../tidal-lyrics-module
python install.py

echo "Installation completed successfully!"
echo "Please configure your API keys in the Tidal plugin settings."
```

### 11.3 User Documentation

```markdown
# Tidal Lyrics Integration - User Guide

## Overview

This extension adds robust lyrics downloading capabilities to TrevTV's Tidal plugin for Lidarr. It automatically retrieves and saves lyrics in .lrc format alongside your music files.

## Features

- Automatic lyrics retrieval during track downloads
- Support for multiple lyrics sources (Musixmatch, Genius, NetEase, lrclib)
- Synchronized lyrics (.lrc) format support
- Intelligent caching to reduce API usage
- Self-hosted service options for complete control

## Setup Guide

### 1. Basic Installation

1. Install the extension using the provided installer:
   ```
   curl -sSL https://raw.githubusercontent.com/yourusername/tidal-lyrics/main/lyrics_install.sh | bash
   ```

2. Restart your Lidarr service:
   ```
   systemctl restart lidarr
   ```

3. In Lidarr, go to Settings > Media Management and ensure:
   - "Import Extra Files" is enabled
   - ".lrc" is added to the list of extra file extensions

### 2. API Configuration

For best results, configure the following free API keys:

#### Musixmatch API
1. Sign up at [Musixmatch for Developers](https://developer.musixmatch.com/signup)
2. Create a new application to get your API key
3. Enter the key in the Lyrics settings page of the Tidal plugin

#### Genius API
1. Sign up at [Genius API Clients](https://genius.com/api-clients)
2. Create a new API client
3. Copy your "Client Access Token" to the Lyrics settings page

### 3. Self-hosted Services (Optional)

The installer automatically sets up:
- Local lrclib.net instance on port 5000
- NetEase API on port 3000

These services will run in Docker containers and provide additional lyrics sources.

## Troubleshooting

### No Lyrics Downloaded

1. Check the logs for API errors:
   ```
   tail -f ~/.config/Lidarr/logs/lyrics.log
   ```

2. Verify your API keys are entered correctly

3. Some songs might not have lyrics available in any of the services

### Cache Management

1. Clear the lyrics cache if you're experiencing issues:
   - Go to the Lyrics settings page
   - Click "Clear Cache"

2. Adjust cache duration if needed (default is 30 days)

## Support

For assistance, please open an issue on our GitHub repository:
[https://github.com/yourusername/tidal-lyrics](https://github.com/yourusername/tidal-lyrics)
```

## 12. Monitoring and Analytics

### 12.1 Performance Monitoring

```python
class LyricsMonitor:
    def __init__(self, config_dir=None):
        """Initialize lyrics performance monitoring"""
        if config_dir is None:
            config_dir = os.path.dirname(os.path.abspath(__file__))
            
        self.db_path = os.path.join(config_dir, 'lyrics_metrics.db')
        self._init_db()
        
    def _init_db(self):
        """Initialize SQLite database for metrics"""
        with sqlite3.connect(self.db_path) as conn:
            cursor = conn.cursor()
            
            # API Requests table
            cursor.execute('''
                CREATE TABLE IF NOT EXISTS api_requests (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp INTEGER NOT NULL,
                    service TEXT NOT NULL,
                    endpoint TEXT NOT NULL,
                    success BOOLEAN NOT NULL,
                    response_time_ms INTEGER NOT NULL,
                    error_message TEXT
                )
            ''')
            
            # Lyrics requests table
            cursor.execute('''
                CREATE TABLE IF NOT EXISTS lyrics_requests (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp INTEGER NOT NULL,
                    artist TEXT NOT NULL,
                    title TEXT NOT NULL,
                    success BOOLEAN NOT NULL,
                    service TEXT,
                    cache_hit BOOLEAN NOT NULL,
                    total_time_ms INTEGER NOT NULL
                )
            ''')
            
            # Create indexes
            cursor.execute('CREATE INDEX IF NOT EXISTS idx_api_service ON api_requests(service)')
            cursor.execute('CREATE INDEX IF NOT EXISTS idx_api_timestamp ON api_requests(timestamp)')
            cursor.execute('CREATE INDEX IF NOT EXISTS idx_lyrics_timestamp ON lyrics_requests(timestamp)')
            
            conn.commit()
    
    async def record_api_request(self, service, endpoint, success, response_time_ms, error_message=None):
        """Record API request metrics"""
        try:
            with sqlite3.connect(self.db_path) as conn:
                cursor = conn.cursor()
                cursor.execute('''
                    INSERT INTO api_requests 
                    (timestamp, service, endpoint, success, response_time_ms, error_message)
                    VALUES (?, ?, ?, ?, ?, ?)
                ''', (
                    int(time.time()),
                    service,
                    endpoint,
                    success,
                    response_time_ms,
                    error_message
                ))
                conn.commit()
        except Exception as e:
            logging.error(f"Error recording API metrics: {str(e)}")
    
    async def record_lyrics_request(self, artist, title, success, service=None, cache_hit=False, total_time_ms=0):
        """Record lyrics request metrics"""
        try:
            with sqlite3.connect(self.db_path) as conn:
                cursor = conn.cursor()
                cursor.execute('''
                    INSERT INTO lyrics_requests 
                    (timestamp, artist, title, success, service, cache_hit, total_time_ms)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                ''', (
                    int(time.time()),
                    artist,
                    title,
                    success,
                    service,
                    cache_hit,
                    total_time_ms
                ))
                conn.commit()
        except Exception as e:
            logging.error(f"Error recording lyrics metrics: {str(e)}")
    
    async def get_api_success_rate(self, service=None, days=7):
        """Get API success rate for the past period"""
        try:
            with sqlite3.connect(self.db_path) as conn:
                cursor = conn.cursor()
                
                timestamp_cutoff = int(time.time()) - (days * 24 * 60 * 60)
                
                if service:
                    cursor.execute('''
                        SELECT COUNT(*), SUM(CASE WHEN success THEN 1 ELSE 0 END)
                        FROM api_requests
                        WHERE timestamp >= ? AND service = ?
                    ''', (timestamp_cutoff, service))
                else:
                    cursor.execute('''
                        SELECT COUNT(*), SUM(CASE WHEN success THEN 1 ELSE 0 END)
                        FROM api_requests
                        WHERE timestamp >= ?
                    ''', (timestamp_cutoff,))
                
                result = cursor.fetchone()
                if result and result[0] > 0:
                    return (result[1] / result[0]) * 100
                return 100.0  # Default to 100% if no data
        except Exception as e:
            logging.error(f"Error getting API success rate: {str(e)}")
            return 0
    
    async def get_service_distribution(self, days=7):
        """Get distribution of successful lyrics by service"""
        try:
            with sqlite3.connect(self.db_path) as conn:
                cursor = conn.cursor()
                
                timestamp_cutoff = int(time.time()) - (days * 24 * 60 * 60)
                
                cursor.execute('''
                    SELECT service, COUNT(*) as count
                    FROM lyrics_requests
                    WHERE timestamp >= ? AND success = 1 AND service IS NOT NULL
                    GROUP BY service
                    ORDER BY count DESC
                ''', (timestamp_cutoff,))
                
                results = cursor.fetchall()
                return {service: count for service, count in results}
        except Exception as e:
            logging.error(f"Error getting service distribution: {str(e)}")
            return {}
    
    async def get_average_response_times(self, days=7):
        """Get average response times by service"""
        try:
            with sqlite3.connect(self.db_path) as conn:
                cursor = conn.cursor()
                
                timestamp_cutoff = int(time.time()) - (days * 24 * 60 * 60)
                
                cursor.execute('''
                    SELECT service, AVG(response_time_ms) as avg_time
                    FROM api_requests
                    WHERE timestamp >= ? AND success = 1
                    GROUP BY service
                    ORDER BY avg_time ASC
                ''', (timestamp_cutoff,))
                
                results = cursor.fetchall()
                return {service: avg_time for service, avg_time in results}
        except Exception as e:
            logging.error(f"Error getting average response times: {str(e)}")
            return {}
```

### 12.2 Automated Health Checks

```python
class LyricsHealthChecker:
    def __init__(self, lyrics_manager):
        """Initialize health checker with lyrics manager"""
        self.lyrics_manager = lyrics_manager
        self.test_tracks = [
            {"artist": "Queen", "title": "Bohemian Rhapsody"},
            {"artist": "Michael Jackson", "title": "Billie Jean"},
            {"artist": "The Beatles", "title": "Hey Jude"},
            {"artist": "Adele", "title": "Rolling in the Deep"},
            {"artist": "Ed Sheeran", "title": "Shape of You"}
        ]
    
    async def check_services_health(self):
        """Check health of all lyrics services"""
        results = {}
        
        for service_info in self.lyrics_manager.services:
            service_name = service_info['name']
            service = service_info['service']
            
            # Skip if service is disabled
            if not service_info.get('enabled', False):
                results[service_name] = {
                    "status": "disabled",
                    "message": "Service is disabled in settings"
                }
                continue
            
            # Check service-specific health
            if service_name == 'lrclib':
                # LRCLib has a health endpoint
                health = await service.health_check()
                results[service_name] = {
                    "status": "healthy" if health else "unhealthy",
                    "message": "Service is responding normally" if health else "Health check failed"
                }
            else:
                # For other services, try a test query
                try:
                    # Pick a random test track
                    test_track = random.choice(self.test_tracks)
                    
                    # Start timer
                    start_time = time.time()
                    
                    # Try to get lyrics
                    lyrics = None
                    if service_name == 'tidal' and hasattr(service, 'get_lyrics'):
                        # Skip Tidal service for health check as it needs track_id
                        results[service_name] = {
                            "status": "unknown",
                            "message": "Cannot test without Tidal track ID"
                        }
                        continue
                    elif service_name == 'musixmatch':
                        track_id = await service.get_track_id(test_track["artist"], test_track["title"])
                        if track_id:
                            lyrics = await service.get_lyrics(track_id)
                    elif service_name == 'genius':
                        lyrics = await service.get_lyrics(test_track["artist"], test_track["title"])
                    elif service_name == 'netease':
                        lyrics = await service.get_lyrics_by_search(test_track["artist"], test_track["title"])
                    
                    # Calculate response time
                    response_time = (time.time() - start_time) * 1000  # ms
                    
                    # Determine status
                    if lyrics:
                        results[service_name] = {
                            "status": "healthy",
                            "message": f"Service returned lyrics in {response_time:.0f}ms",
                            "response_time_ms": response_time
                        }
                    else:
                        results[service_name] = {
                            "status": "degraded",
                            "message": f"Service responded but found no lyrics for test track",
                            "response_time_ms": response_time
                        }
                        
                except Exception as e:
                    results[service_name] = {
                        "status": "unhealthy",
                        "message": f"Error: {str(e)}",
                        "error": str(e)
                    }
        
        return results
    
    async def run_scheduled_check(self):
        """Run a scheduled health check and log results"""
        health_results = await self.check_services_health()
        
        # Log results
        for service_name, result in health_results.items():
            if result["status"] == "healthy":
                logging.info(f"Service health: {service_name} is {result['status']} - {result['message']}")
            elif result["status"] == "degraded":
                logging.warning(f"Service health: {service_name} is {result['status']} - {result['message']}")
            elif result["status"] == "unhealthy":
                logging.error(f"Service health: {service_name} is {result['status']} - {result['message']}")
            else:
                logging.info(f"Service health: {service_name} is {result['status']} - {result['message']}")
        
        # Return overall status
        healthy_count = sum(1 for r in health_results.values() if r["status"] == "healthy")
        total_services = len([s for s in self.lyrics_manager.services if s["enabled"]])
        
        if healthy_count == total_services:
            return "healthy"
        elif healthy_count > 0:
            return "degraded"
        else:
            return "unhealthy"
```

## 13. Conclusion and Recommendations

The technical design outlined in this document provides a comprehensive, reliable, and free solution for integrating lyrics functionality into TrevTV's Tidal plugin for Lidarr. By leveraging multiple free services with intelligent fallback mechanisms, the system ensures high availability of lyrics even when individual services experience downtime.

### 13.1 Key Architectural Benefits

1. **Redundancy** - Multiple services ensure lyrics availability from different sources
2. **Caching** - Local storage reduces API calls and improves performance
3. **Extensibility** - Modular design allows easy addition of new lyrics sources
4. **Self-hosting** - Options for local deployment eliminate external dependencies
5. **Optimization** - Asynchronous processing and connection pooling deliver excellent performance
6. **Security** - Encryption for API keys and proper rate limiting to prevent service abuse

### 13.2 Implementation Recommendations

1. **Phased Rollout**:
   - Phase 1: Integrate basic Musixmatch and Genius APIs
   - Phase 2: Add local caching and fallback mechanisms
   - Phase 3: Implement self-hosted services for complete autonomy

2. **API Management**:
   - Register for both Musixmatch and Genius free tiers
   - Implement careful rate limiting to avoid exceeding quotas
   - Consider a user-provided API key model for shared instances

3. **Performance Optimization**:
   - Use connection pooling to minimize connection overhead
   - Implement aggressive caching with 30-day TTL for frequently accessed lyrics
   - Batch process lyrics retrieval during library scans

4. **Monitoring**:
   - Schedule periodic health checks to detect service outages
   - Collect metrics on API performance and success rates
   - Alert on sustained service failures

### 13.3 Future Roadmap

The design includes several future enhancements that can be implemented after the core functionality is stable:

1. Community lyrics contribution system
2. LRC editing interface for manual corrections
3. Multi-language support for translated lyrics
4. Audio fingerprinting for improved metadata matching

By following this technical design, TrevTV's Tidal plugin will gain robust lyrics support comparable to commercial services, while remaining completely free and highly reliable through its multi-service architecture.

---
Answer from Perplexity: pplx.ai/share