# Lidarr Tidal Plugin: 2-Year Development Plan

## Table of Contents
- [Overview](#overview)
- [Phase 0: Technical Debt Reduction](#phase-0-technical-debt-reduction)
- [Phase 1: Foundation Enhancement](#phase-1-foundation-enhancement)
- [Phase 2: Integration & Scaling](#phase-2-integration--scaling)
- [Phase 3: Advanced Features](#phase-3-advanced-features)
- [Implementation Strategy](#implementation-strategy)
- [OVER-ENGINEERED DREAMS OR NON-REALISTIC ROADMAP SECTION](#over-engineered-dreams-or-non-realistic-roadmap-section)

## Overview
This plan outlines the strategic development of the Lidarr Tidal plugin over the next 2 years, with structured phases, milestones, and sprints following TDD methodology.

## Phase 0: Technical Debt Reduction (Q1 2023: Jan-Mar)

This initial phase focuses on addressing critical technical debt before proceeding with new feature development. Reducing this debt is essential for ensuring a stable foundation for future enhancements.

### Milestone 0: Core Stability and Security
**Success Metrics:**
- 50% reduction in memory usage during large downloads
- Zero security vulnerabilities in credential handling
- 90% test coverage for core components

#### Sprint 0.1 (2 weeks): Memory Management and Resource Optimization

**Objective:** Eliminate memory leaks and optimize resource usage in the download pipeline

**Tasks:**
- Implement buffer pooling for large file downloads
- Add memory pressure monitoring and adaptive throttling
- Fix resource leaks in error paths and cancellation scenarios
- Implement proper disposal patterns throughout the codebase
- Add memory usage telemetry and alerting

**Deliverables:**
- Refactored `DownloadTaskQueue` with optimized memory management
- Memory usage benchmarks showing improvement
- Stress test results demonstrating stability under load

#### Sprint 0.2 (2 weeks): Security Enhancements

**Objective:** Enhance authentication security within Lidarr plugin constraints while preserving functionality.

**Tasks:**
- Implement plugin-compatible credential storage using Lidarr's built-in secure storage mechanisms
- Create fallback authentication methods that respect Lidarr's plugin sandboxing
- Develop token refresh logic that works within plugin lifecycle events
- Add graceful re-authentication when sessions expire during background operations

**Deliverables:**
- Secure credential storage implementation
- Encrypted token management system
- Security audit report with vulnerabilities addressed

#### Sprint 0.3 (2 weeks): Error Handling and Recovery

**Objective:** Improve system resilience and recovery capabilities

**Tasks:**
- Categorize error types (transient, permanent, authentication, etc.)
- Implement intelligent retry strategies based on error types
- Enhance state recovery mechanisms for interrupted operations
- Add comprehensive error logging with contextual information
- Create user-friendly error messages and recovery suggestions

**Deliverables:**
- Error handling framework with categorization
- Improved retry logic with exponential backoff
- Robust state recovery implementation
- Enhanced logging system with error context

#### Sprint 0.4 (2 weeks): Architectural Foundations

**Objective:** Begin refactoring toward a more maintainable architecture

**Tasks:**
- Extract core responsibilities from monolithic classes
- Implement proper dependency injection throughout codebase
- Create abstraction layers for external dependencies
- Apply SOLID principles to core components
- Develop architectural documentation and guidelines

**Deliverables:**
- Refactored core components with clear responsibilities
- Dependency injection framework implementation
- Abstraction layers for external services
- Architectural documentation with patterns and practices

#### Sprint 0.5 (2 weeks): Exception Handling Standardization

**Objective:** Implement consistent exception handling patterns throughout the codebase

**Tasks:**
- Create standardized exception types for different failure categories (API, network, authentication, etc.)
- Implement centralized exception logging with contextual information
- Add structured error reporting for better diagnostics
- Develop user-friendly error messages with actionable recovery steps
- Implement global exception handlers for unhandled exceptions

**Deliverables:**
- Exception handling framework
- Improved error logging system
- User-friendly error messages
- Exception analytics dashboard

#### Sprint 0.6 (2 weeks): Memory Management Optimization

**Objective:** Address memory leaks and optimize resource usage, particularly for large downloads

**Tasks:**
- Implement proper buffer pooling for audio stream processing
- Add memory pressure detection and adaptive throttling
- Optimize large object handling to reduce GC pressure
- Implement proper disposal patterns throughout async operations
- Add memory usage telemetry for early detection of issues

**Deliverables:**
- Buffer pooling implementation
- Memory pressure monitoring system
- Optimized large object handling
- Memory usage dashboard

#### Sprint 0.7 (2 weeks): Logging and Telemetry Enhancement

**Objective:** Improve observability and diagnostics capabilities

**Tasks:**
- Implement structured logging throughout the codebase
- Add correlation IDs for tracking operations across components
- Create performance metrics collection for critical operations
- Develop log level management for controlling verbosity
- Implement log rotation and archiving for long-running instances

**Deliverables:**
- Structured logging implementation
- Correlation tracking system
- Performance metrics dashboard
- Log management UI

#### Sprint 0.8 (2 weeks): Concurrency Model Refinement

**Objective:** Improve the thread safety and concurrency patterns throughout the download pipeline

**Tasks:**
- Refactor thread synchronization mechanisms to use modern patterns (e.g., AsyncLock instead of SemaphoreSlim where appropriate)
- Implement proper cancellation token propagation throughout all async operations
- Add deadlock detection and prevention mechanisms
- Optimize concurrent collections usage for better performance
- Implement resource throttling based on system capabilities

**Deliverables:**
- Modernized concurrency implementation
- Comprehensive cancellation support
- Deadlock prevention system
- Concurrency monitoring dashboard

#### Sprint 0.9 (2 weeks): Configuration Management Overhaul

**Objective:** Improve how configuration is managed, validated, and applied throughout the system

**Tasks:**
- Implement strong typing for all configuration objects
- Add configuration validation with meaningful error messages
- Create runtime configuration update capabilities
- Develop configuration migration system for version upgrades
- Implement configuration backup and restore functionality

**Deliverables:**
- Strongly-typed configuration system
- Configuration validation framework
- Runtime configuration management
- Configuration backup/restore tools

#### Sprint 1.0 (2 weeks): State Management Improvement

**Objective:** Enhance how download state is managed, persisted, and recovered

**Tasks:**
- Implement atomic state transitions with proper validation
- Create robust serialization/deserialization for download queue state
- Add incremental state persistence to prevent data loss
- Develop state recovery mechanisms for unexpected shutdowns
- Implement state consistency validation on startup

**Deliverables:**
- State machine implementation for downloads
- Robust state persistence system
- Recovery mechanisms for interrupted operations
- State consistency validation tools

Phase 1: Foundation Enhancement (Months 1-6)

### Milestone 1: Technical Debt Reduction

#### Sprint 0.3 (2 weeks): Rate Limiting & Throttling Refinement

**Objective:** Enhance the existing rate limiting and download throttling mechanisms for improved reliability and performance.

**Tasks:**
- Refactor token bucket implementation to support hierarchical rate limits (API-level, album-level, track-level)
- Implement adaptive token replenishment based on API response headers
- Add centralized rate limit orchestration to prevent distributed components from overwhelming the API
- Enhance circuit breaker pattern implementation for more granular failure detection
- Implement request categorization and prioritization (searches vs downloads)
- Add detailed telemetry for rate limit consumption and throttling events

**Deliverables:**
- Enhanced token bucket implementation with hierarchical support
- Centralized rate limiting orchestrator
- Request priority management system
- Rate limit visualization in UI
- Comprehensive logging for rate limit events

#### Sprint 0.4 (2 weeks): Download Concurrency Optimization

**Objective:** Optimize the concurrent download system to better adapt to system conditions and API limitations.

**Tasks:**
- Enhance `SemaphoreSlim` usage with dynamic concurrency limits based on system load
- Implement adaptive throttling that responds to network conditions and API response times
- Add resource monitoring (CPU, memory, network) to inform throttling decisions
- Refine the retry mechanism with categorized backoff strategies based on error types
- Implement more sophisticated failure tracking with per-track circuit breakers
- Create a download prioritization system that respects user-defined importance

**Deliverables:**
- Dynamic concurrency management system
- Resource-aware throttling implementation
- Enhanced retry system with error categorization
- Per-track circuit breaker implementation
- Download priority configuration UI

### Milestone 2: User Experience Improvements

#### Sprint 1 (2 weeks): TidalDownloadViewer Implementation

**Objective:** Create an intuitive, responsive UI for monitoring and managing downloads.

**Tasks:**
- Develop real-time download progress visualization
- Implement download management controls (pause/resume/cancel)
- Create filtering and sorting capabilities
- Add detailed view for individual downloads
- Implement responsive design for various screen sizes

**Deliverables:**
- Functional TidalDownloadViewer component
- User documentation for download management
- Automated UI tests for component

#### Sprint 2 (2 weeks): Metadata Enhancement

**Objective:** Improve metadata handling and display throughout the application.

**Tasks:**
- Enhance album artwork embedding
- Improve lyrics support with .lrc files
- Add support for additional metadata fields
- Implement metadata preview functionality
- Create metadata editor for manual adjustments

**Deliverables:**
- Enhanced metadata processing pipeline
- Support for extended ID3 tags
- Improved artwork quality and embedding

### Milestone 2: Core Performance Improvements

#### Sprint 3 (2 weeks): Intelligent Caching System

**Objective:** Implement a sophisticated caching system to reduce API calls and improve response times.

**Tasks:**
- Develop multi-level cache architecture (memory and disk)
- Implement artist catalog caching with configurable TTL
- Create album metadata caching with invalidation strategy
- Add track listing cache with version tracking
- Implement cache statistics and monitoring

**Deliverables:**
- Complete caching subsystem with configurable parameters
- Cache hit/miss analytics dashboard
- Documentation for cache management
- Performance benchmarks showing improvement

#### Sprint 4 (2 weeks): Advanced Rate Limiting Orchestration

**Objective:** Create a centralized rate limiting system that intelligently manages all API interactions.

**Tasks:**
- Implement request prioritization framework (searches vs downloads)
- Develop adaptive rate limiting based on API response headers
- Create request queuing system with priority levels
- Implement parallel request optimization with dynamic throttling
- Add detailed telemetry for rate limit monitoring

**Deliverables:**
- Centralized rate limiting orchestrator
- Request priority management system
- Rate limit visualization in UI
- Documentation for rate limit configuration

#### Sprint 5 (2 weeks): Preemptive Data Loading

**Objective:** Improve perceived performance by intelligently preloading likely-to-be-needed data.

**Tasks:**
- Implement background loading of related artists
- Create smart preloading of album details when browsing artist
- Develop user behavior analysis to predict navigation patterns
- Add configurable preloading depth and aggressiveness
- Implement resource-aware preloading that respects system load

**Deliverables:**
- Preemptive loading subsystem
- User behavior tracking (anonymized)
- Configuration UI for preloading settings
- Performance impact analysis

### Milestone 2: Download Engine Improvements

#### Sprint 3 (2 weeks): Download Pipeline Optimization

**Objective:** Enhance download performance, reliability, and resource efficiency.

**Tasks:**
- Implement adaptive buffer management
- Add download resumption capabilities
- Create intelligent retry mechanisms
- Develop download verification system
- Implement progress reporting improvements

**Deliverables:**
- Optimized download pipeline with 30% better performance
- Improved reliability metrics for interrupted downloads
- Memory usage reduction of 40% for large downloads
Phase 2: Integration & Scaling (Months 7-12)

### Milestone 3: User Experience Enhancements

#### Sprint 7 (2 weeks): Smart Duplicate Detection
**Objective:** Prevent redundant downloads and identify existing content

**Tasks:**
- Implement fingerprinting for audio content comparison
- Create detection for same tracks with different metadata
- Develop quality comparison for upgrade decisions
- Add user notification for potential duplicates
- Implement override options for edge cases

**Deliverables:**
- Duplicate detection engine
- User interface for duplicate management
- Documentation for duplicate handling

#### Sprint 8 (2 weeks): Download Resume Capability
**Objective:** Enable interrupted downloads to continue without starting over

**Tasks:**
- Implement partial file detection
- Develop byte range request handling
- Create download state persistence
- Add integrity verification for resumed downloads
- Implement cleanup for abandoned partials

**Deliverables:**
- Resume-capable download manager
- Progress tracking for partial downloads
- Documentation for the resume feature

#### Sprint 9 (2 weeks): Adaptive Quality Selection
**Objective:** Implement intelligent quality selection based on availability and user preferences.

**Tasks:**
- Create smart fallback when preferred quality isn't available
- Implement configurable quality profiles specific to Tidal's offerings
- Develop bandwidth-aware quality switching during peak/off-peak hours
- Add quality comparison preview before download
- Implement quality upgrade paths for existing content

**Deliverables:**
- Quality selection algorithm
- Profile configuration UI
- Bandwidth scheduling system
- Quality comparison visualization

### Milestone 4: Advanced User Controls

#### Sprint 9 (2 weeks): Batch Operations UI
**Objective:** Enable efficient management of multiple downloads simultaneously

**Tasks:**
- Develop multi-select interface for downloads
- Implement batch actions (pause/resume/cancel/retry)
- Create confirmation dialogs for destructive actions
- Add keyboard shortcuts for common operations
- Implement status filtering for batch selection

**Deliverables:**
- Enhanced download management UI
- Batch operation documentation
- Keyboard shortcut reference

#### Sprint 10 (2 weeks): Bandwidth Scheduling
**Objective:** Allow users to control when downloads occur to manage network impact

**Tasks:**
- Create time-based download scheduling
- Implement bandwidth throttling controls
- Develop priority-based queue management
- Add schedule templates for common patterns
- Create override mechanisms for urgent downloads

**Deliverables:**
- Scheduling interface
- Bandwidth management controls
- Documentation for network management
Milestone 4: Performance Optimization
Sprint 10 (2 weeks): Download Optimization

Enhance concurrent download management
Implement adaptive throttling based on system load
Optimize token bucket algorithm for rate limiting
Sprint 11 (2 weeks): Memory & Storage Optimization

Improve memory usage during large downloads
Enhance queue persistence mechanism
Implement storage management features
Sprint 12 (2 weeks): API Optimization

Optimize Tidal API interactions
Implement connection pooling
Enhance error recovery mechanisms
Phase 3: Advanced Features (Months 13-18)

### Milestone 5: Smart Download Management

#### Sprint 18 (2 weeks): Human Behavior Simulation Enhancements
**Objective:** Further refine the natural download behavior simulation to avoid detection

**Tasks:**
- Enhance time-of-day adaptation with more sophisticated patterns
- Implement genre and year-based track selection patterns
- Add natural playback speed variation simulation
- Develop track repeat and skip behavior modeling
- Create adaptive session patterns based on historical data

**Deliverables:**
- Enhanced UserBehaviorSimulator implementation
- Time-pattern visualization tools
- Configuration UI for behavior patterns
- Behavior analytics dashboard

### Milestone 6: Extended Platform Support

#### Sprint 19 (2 weeks): Country Code Optimization
**Objective:** Optimize API access based on geographic routing

**Tasks:**
- Implement automatic country code selection based on performance
- Create fallback routing when primary country experiences issues
- Develop performance metrics for different geographic endpoints
- Add rotation capabilities to distribute load
- Implement geographic-aware rate limiting

**Deliverables:**
- Geographic optimization system
- Performance measurement for endpoints
- Automatic routing selection
- Geographic analytics dashboard

#### Sprint 20 (2 weeks): Token Bucket Enhancement
**Objective:** Refine rate limiting with advanced token bucket implementation

**Tasks:**
- Implement hierarchical token buckets (track/album/artist levels)
- Create adaptive bucket sizes based on historical performance
- Develop priority-based token allocation
- Add detailed token usage analytics
- Implement predictive token replenishment

**Deliverables:**
- Enhanced token bucket implementation
- Token usage visualization
- Priority configuration interface
- Token analytics system

### Milestone 4: Quality of Life Enhancements

#### Sprint 10 (2 weeks): Advanced Metadata Management with Beets Integration

**Objective:** Implement Beets integration for superior metadata tagging before Lidarr processing.

**Tasks:**
- Develop Beets integration layer for automated tagging
- Create configurable Beets profiles for different music genres
- Implement pre-processing pipeline that runs Beets before notifying Lidarr
- Add metadata preview and manual override options
- Develop fallback mechanisms when Beets is unavailable

**Deliverables:**
- Beets integration module
- Configuration UI for Beets settings
- Metadata comparison tool showing before/after tags
- Documentation for Beets integration

#### Sprint 11 (2 weeks): Smart Library Organization

**Objective:** Enhance organization of downloaded content with intelligent file naming and folder structures.

**Tasks:**
- Implement customizable file naming templates
- Create smart folder organization based on genre/decade/mood
- Develop duplicate file detection and management
- Add support for multi-disc album organization
- Implement various artist compilation handling

**Deliverables:**
- File organization configuration UI
- Template system for naming conventions
- Preview tool for file organization

#### Sprint 12 (2 weeks): User Experience Enhancements

**Objective:** Improve overall user experience with intuitive interfaces and helpful features.

**Tasks:**
- Implement detailed download history with filtering and search
- Create visual download queue with drag-and-drop prioritization
- Develop notification system for important events
- Add batch operations for queue management
- Implement keyboard shortcuts for power users

**Deliverables:**
- Enhanced download history UI
- Interactive queue management interface
- Notification center
- Keyboard shortcut documentation

#### Sprint 13 (2 weeks): Smart Download Scheduling

**Objective:** Implement intelligent download scheduling to optimize bandwidth usage and system resources.

**Tasks:**
- Develop time-based download scheduling (night/weekend focus)
- Create bandwidth usage monitoring and throttling
- Implement priority-based scheduling for must-have content
- Add system load detection to pause downloads during high usage
- Develop calendar view for scheduled downloads

**Deliverables:**
- Scheduling configuration interface
- Bandwidth monitoring dashboard
- Calendar view for download planning

## Phase 4: Future-Proofing (Months 19-24)

### Milestone 7: Security & Compliance

#### Sprint 21 (2 weeks): Enhanced Credential Management
**Objective:** Improve security of authentication and credential handling

**Tasks:**
- Implement secure credential storage using encryption
- Develop token refresh logic that works within plugin lifecycle
- Create fallback authentication methods
- Add secure token rotation
- Implement audit logging for authentication events

**Deliverables:**
- Secure credential storage implementation
- Token management system
- Authentication audit system
- Security documentation

### Milestone 8: Ecosystem Expansion
Sprint 22 (2 weeks): Plugin Architecture

Develop plugin system for extensions
Create developer documentation
Implement plugin marketplace
Sprint 23 (2 weeks): Integration Expansion

Add support for additional music services
Implement cross-service synchronization
Develop unified library management
Sprint 24 (2 weeks): Community Features

Create community sharing capabilities
Implement recommendation system
Develop collaborative playlists

Implementation Strategy
For each sprint, we'll follow this implementation strategy:

### Sprint Planning
- Define specific features to implement
- Create test cases for each feature
- Establish acceptance criteria
- Allocate resources and set priorities
- Identify dependencies and risks

### Development Cycle
- Write failing tests for the feature
- Implement the feature to pass tests
- Refactor and optimize
- Document the implementation
- Conduct code reviews

### Sprint Review
- Demonstrate working features
- Collect feedback from stakeholders
- Validate against acceptance criteria
- Update documentation
- Update roadmap if necessary

### Sprint Retrospective
- Identify what went well
- Address challenges and blockers
- Implement process improvements
- Update estimation accuracy
- Plan knowledge sharing

### Quality Assurance
- Run automated test suites
- Perform manual testing for edge cases
- Validate performance metrics
- Check for regression issues
- Ensure documentation is updated

### Deployment
- Create release notes
- Package for distribution
- Deploy to staging environment
- Perform smoke tests
- Deploy to production
- Monitor for issues

# OVER-ENGINEERED DREAMS OR NON-REALISTIC ROADMAP SECTION 🚀🤪

### Milestone 3: Practical Enhancements

#### Sprint 8 (2 weeks): Simplified Beets Integration - Phase 1

**Objective:** Implement basic Beets integration as an optional feature with minimal complexity.

**Tasks:**
- Create external Beets dependency detection and validation
- Develop simple setup wizard for Beets installation
- Implement command-line execution wrapper for Beets
- Add basic configuration UI for essential Beets settings
- Create documentation for manual Beets installation

**Deliverables:**
- Beets detection and validation system
- Setup wizard for external Beets installation
- Basic command-line integration
- Simple configuration interface

#### Sprint 9 (2 weeks): Simplified Rate Limiting

**Objective:** Implement effective but simple rate limiting to prevent API throttling.

**Tasks:**
- Develop configurable global rate limiter with fixed limits
- Implement basic retry logic with exponential backoff
- Add simple request categorization (critical vs. non-critical)
- Create basic monitoring for rate limit consumption
- Implement manual override for emergency situations

**Deliverables:**
- Global rate limiting system
- Configurable retry mechanism
- Basic rate limit monitoring UI

#### Sprint 10 (2 weeks): Refined Download Behavior

**Objective:** Improve download patterns to avoid triggering Tidal's automation detection.

**Tasks:**
- Implement configurable randomized delays between downloads
- Add time-of-day download scheduling (day/night/weekend patterns)
- Develop simple download caps per time period
- Create "listening simulation" mode that mimics human consumption patterns
- Add jitter to API request timing to avoid predictable patterns

**Deliverables:**
- Randomized delay implementation
- Time-based download scheduling
- Download pattern configuration UI
- Basic listening simulation mode

#### Sprint 11 (2 weeks): Simple Caching Implementation

**Objective:** Improve performance with straightforward caching mechanisms.

**Tasks:**
- Implement LRU cache for recently accessed artists and albums
- Add basic prefetching of album details when viewing artists
- Create simple time-based cache expiration
- Develop cache statistics for monitoring effectiveness
- Add manual cache clearing options

**Deliverables:**
- LRU caching implementation
- Basic prefetching system
- Cache monitoring interface

#### Sprint 12 (2 weeks): Practical Concurrency Management

**Objective:** Ensure stable concurrent operations without over-engineering.

**Tasks:**
- Refine semaphore usage for download limiting
- Implement comprehensive cancellation token support
- Add configurable limits for parallel operations
- Create basic deadlock detection with timeout-based recovery
- Implement simple resource monitoring to adjust concurrency

**Deliverables:**
- Improved semaphore implementation
- Cancellation support throughout the codebase
- Configuration UI for concurrency settings
- Basic deadlock recovery system

### Future Enhancements (Post 1.0)

#### Advanced Beets Integration - Phase 2

**Objective:** Enhance Beets integration with more automated features while maintaining simplicity.

**Tasks:**
- Develop automated Beets installation for common platforms
- Create genre-specific tagging profiles
- Implement metadata preview and comparison tools
- Add batch processing capabilities

#### Advanced Rate Limiting (When Needed)

**Objective:** Implement more sophisticated rate limiting only if simple approach proves insufficient.

**Tasks:**
- Develop hierarchical token buckets if needed
- Implement adaptive limits based on API response headers
- Create more granular request categorization

Milestone 2000 - Never gonna happen, or maybe.

> _"We put these ideas here so we can dream big, but let's be honest - they'll probably never happen!"_ 😂

## 🧠 Fantasy AI Features 

### AI-Based Download Prioritization 🤖
- Implement machine learning for download prioritization
- Develop user preference learning
- Create adaptive scheduling based on usage patterns
- _Reality check: A simple dropdown menu would work just fine!_ 🙄

### Predictive Caching 🔮
- Develop predictive algorithms for popular content
- Implement smart pre-caching
- Add bandwidth usage optimization
- _Reality check: We can barely predict what we'll have for lunch tomorrow!_ 🍕

### Advanced Rate Limiting with Machine Learning 📊
- Enhance rate limiting with machine learning
- Implement adaptive session management
- Develop IP rotation capabilities
- _Reality check: A token bucket with some if-statements will do 99% of what we need!_ 👍

## 🏗️ Architectural Pipe Dreams

### Plugin Architecture for our Plugin 🤯
- Develop plugin system for extensions
- Create developer documentation
- Implement plugin marketplace
- _Reality check: Yo dawg, I heard you like plugins, so we put plugins in your plugin!_ 🎸

### End-to-End Encryption for Everything 🔒
- Implement end-to-end encryption for sensitive data
- Enhance authentication mechanisms
- Add security auditing features
- _Reality check: Lidarr already handles security, we're just downloading music!_ 🎵

### Enterprise-Grade Access Control 👮‍♂️
- Create role-based access control
- Implement user management
- Add activity logging
- _Reality check: It's a personal media server plugin, not a corporate intranet!_ 💼

### Cross-Service Music Ecosystem 🌐
- Add support for additional music services
- Implement cross-service synchronization
- Develop unified library management
- _Reality check: Let's master one service before trying to rule them all!_ 👑

### Social Music Network 👥
- Create community sharing capabilities
- Implement recommendation system
- Develop collaborative playlists
- _Reality check: We're a download plugin, not Spotify!_ 🎧
