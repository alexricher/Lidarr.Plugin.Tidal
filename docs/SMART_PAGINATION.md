# Smart Pagination for Tidal Searches

## Overview

Smart Pagination is an advanced feature in the Tidal Plugin that intelligently determines how many search result pages to retrieve based on the context of the search. This enables more comprehensive searches for artists with extensive catalogs while remaining efficient for focused searches.

## How It Works

The Smart Pagination system:

1. **Analyzes search intent** - Determines whether you're looking for a specific album, exploring an artist discography, etc.
2. **Starts conservatively** - Begins with the standard number of pages (3 by default)
3. **Evaluates result quality** - Analyzes the results from initial pages
4. **Makes intelligent decisions** - Continues pagination only when there's a high probability of finding valuable additional content
5. **Respects system limits** - Adjusts to your rate limiting settings to avoid overwhelming the Tidal API

## Configuration Options

Smart Pagination can be configured in the Tidal Indexer settings:

### Basic Settings

- **Enable Smart Pagination** - Toggles the feature on/off (default: on)
- **Max Search Pages** - The absolute maximum pages to retrieve, even with smart pagination (default: 10)
- **Search Thoroughness** - Controls how aggressive the search should be:
  - **Efficient**: Minimizes API calls (max 5 pages)
  - **Balanced**: Default option (max 8 pages)
  - **Thorough**: For enthusiasts (max 12 pages)
  - **Completionist**: Maximum thoroughness (max 20 pages)

### Advanced Rate Limiting

Smart Pagination works in conjunction with rate limiting settings:

- **Max Concurrent Searches** - Limits how many searches can run at once
- **Max Requests Per Minute** - Prevents overwhelming the Tidal API

## When To Use Smart Pagination

Smart Pagination is especially valuable for:

1. **Prolific artists** - Artists like John Zorn, Buckethead, or Miles Davis with hundreds of releases
2. **Classical composers** - Classical composers often have thousands of recordings
3. **Common artist names** - Artists with common names need deeper searching to find specific matches
4. **Complete discographies** - Building complete collections often requires finding lesser-known releases
5. **Genre exploration** - Certain genres (classical, jazz, electronic) have more extensive catalogs

## Implementation Details

The Smart Pagination system includes:

- **Search intent detection** - Analyzes query structure and content
- **Artist profile analysis** - Recognizes prolific artists who need deeper searching
- **Diminishing returns detection** - Stops when additional pages yield little value
- **Genre-aware searching** - Adjusts depth based on musical genre

## Performance Considerations

Smart Pagination includes several optimizations to minimize impact:

- **Rate limiting** - Respects Tidal's API rate limits
- **Cancellation support** - Gracefully stops when searches are cancelled
- **Efficient page processing** - Minimizes memory usage for large result sets
- **Timeout protection** - Prevents searches from hanging indefinitely

## Troubleshooting

If you experience any issues with Smart Pagination:

1. **Check logs** - Look for messages with 'Smart pagination' in your Lidarr logs
2. **Adjust settings** - Try reducing Search Thoroughness if you experience rate limiting
3. **Balance with rate limits** - Ensure Max Requests Per Minute aligns with your Tidal account

## Future Enhancements

Planned improvements include:

1. **User-driven learning** - Track which artists typically need deeper searching
2. **Discography completion analysis** - Detect gaps in artist discographies
3. **Search term refinement** - Suggest better search terms when needed
4. **Release timeline visualization** - Show timeline of found releases
5. **Artist relationship mapping** - Use knowledge of collaborators to improve search depth 