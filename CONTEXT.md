# Domain Glossary

## Core Concepts

### Video
A YouTube video entity, identified by its unique video ID, with metadata including title, author/channel, duration, view count, publication timestamp, and thumbnail.

### Channel
A YouTube creator channel, identified by channel ID or handle, containing channel metadata and a paginated list of uploaded videos.

### Video Details
The comprehensive metadata for a specific video, including full description, channel information, engagement statistics (likes, dislikes), and available chapters.

### Comment
A viewer comment on a video, with author information, publish timestamp, like count, reply count, and optional author badges.

### Session
An authenticated YouTube user session represented by Netscape-formatted browser cookies, captured via isolated sign-in or manual import, and securely stored in the desktop keyring.

### Cookie File Lease
A short-lived, permission-restricted (`0600`) temporary filesystem representation of an active session, acquired during authenticated tool execution and immediately released upon completion.

### Home Recommendations
The personalized video recommendations feed for an authenticated user session.

### Watch History
The record of previously viewed videos associated with an authenticated user session.

### Queue
An in-memory, ordered playlist of videos queued for back-to-back playback.

### YouTube Playback Progress
The viewer playback state supplied by YouTube, including display watch progress, a separate resume position, and YouTube's completed status. It is unavailable when YouTube does not include viewer-specific state.
