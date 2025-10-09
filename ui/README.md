# Silo File Management - UI Documentation

## Overview

The Silo File Management UI is a comprehensive, modern web application featuring:

- **Dark Theme**: Sleek, eye-friendly dark color scheme with light theme option
- **Comprehensive Iconography**: Font Awesome icons throughout for visual clarity
- **Error Handling**: User-friendly error messages with toast notifications
- **Interactive UI**: Drag-and-drop uploads, real-time updates, and responsive design
- **Rich Interface**: File management, search, analytics, and admin dashboards

## Features

### 1. Authentication
- **Login/Signup Forms**: Beautiful, accessible forms with validation
- **Password Strength Indicator**: Visual feedback for password quality
- **Remember Me**: Persistent sessions
- **Token Management**: JWT token handling with automatic refresh

### 2. File Management
- **Grid & List Views**: Toggle between different file display modes
- **Drag & Drop Upload**: Intuitive file uploading
- **File Actions**: Download, delete, view metadata
- **Context Menu**: Right-click for quick actions
- **Real-time Updates**: Files list updates after upload

### 3. Advanced Search
- **Multiple Filters**: Search by name, extension, date, size
- **Visual Results**: Grid display of search results
- **Quick Search**: Top bar quick search with debouncing

### 4. Analytics Dashboard
- **Usage Statistics**: Total files, storage used, daily uploads/downloads
- **File Type Distribution**: Visual breakdown of file types
- **Storage Quota**: Real-time storage usage monitoring

### 5. Theme System
- **Dark Theme**: Default professional dark theme
- **Light Theme**: Alternative light theme
- **Persistent Preference**: Theme choice saved to localStorage
- **Smooth Transitions**: Animated theme switching

### 6. Error Handling
- **Toast Notifications**: Non-intrusive success/error/warning messages
- **Form Validation**: Real-time input validation
- **API Error Handling**: User-friendly error messages
- **Loading States**: Visual feedback during operations

### 7. Responsive Design
- **Mobile-First**: Works on all screen sizes
- **Adaptive Layout**: Sidebar collapses on mobile
- **Touch-Friendly**: Large tap targets for mobile users

## Directory Structure

```
ui/
├── index.html              # Main application HTML
├── styles/
│   ├── main.css           # Core styles and layout
│   └── dark-theme.css     # Dark theme color overrides
└── scripts/
    ├── config.js          # Configuration constants
    ├── utils.js           # Utility functions
    ├── api.js             # API client
    ├── auth.js            # Authentication module
    ├── files.js           # File management
    ├── upload.js          # Upload functionality
    ├── search.js          # Search functionality
    ├── analytics.js       # Analytics dashboard
    └── app.js             # Main application logic
```

## Configuration

Edit `scripts/config.js` to configure:

```javascript
const CONFIG = {
    API_BASE_URL: 'http://localhost:5289/api',  // API endpoint
    MAX_FILE_SIZE: 100 * 1024 * 1024,           // 100MB max
    TOAST_DURATION: 5000,                        // 5 second toasts
    // ... more options
};
```

## Usage

### Running Locally

1. **Start the API server** (see main README)
2. **Serve the UI** using any web server:
   ```bash
   # Using Python
   cd ui
   python -m http.server 8080
   
   # Using Node.js
   npx http-server ui -p 8080
   
   # Using PHP
   php -S localhost:8080 -t ui
   ```
3. **Open browser** to `http://localhost:8080`

### First Time Setup

1. Click "Sign Up" tab
2. Enter email, username, and password
3. Click "Create Account"
4. Switch to "Login" tab
5. Enter credentials and login

### Uploading Files

1. Navigate to "Upload" section
2. **Option 1**: Drag files onto the drop zone
3. **Option 2**: Click "Browse Files" to select
4. Click "Start Upload" to begin uploading
5. View progress for each file

### Searching Files

1. Navigate to "Search" section
2. Enter search criteria:
   - Search query
   - File extensions (comma-separated)
   - Date range
   - Size range
3. Click "Search"
4. View results in grid format

### Viewing Analytics

1. Navigate to "Analytics" section
2. View statistics:
   - Total files count
   - Storage usage
   - Daily upload/download counts
   - File type distribution

## Customization

### Colors

Edit CSS variables in `styles/main.css`:

```css
:root {
    --color-primary: #6366f1;      /* Primary brand color */
    --color-secondary: #8b5cf6;    /* Secondary color */
    --color-success: #10b981;      /* Success green */
    --color-warning: #f59e0b;      /* Warning orange */
    --color-error: #ef4444;        /* Error red */
}
```

### Dark Theme Colors

Edit `styles/dark-theme.css` to customize dark theme:

```css
.dark-theme {
    --color-bg-primary: #111827;   /* Main background */
    --color-bg-secondary: #1f2937; /* Card background */
    --color-text-primary: #f9fafb; /* Primary text */
}
```

### Adding New Views

1. Add HTML section in `index.html`:
   ```html
   <div id="myNewView" class="view">
       <!-- Content here -->
   </div>
   ```

2. Add navigation item:
   ```html
   <a href="#" class="nav-item" data-view="myNew">
       <i class="fas fa-icon"></i>
       <span>My Feature</span>
   </a>
   ```

3. Create JavaScript module in `scripts/my-feature.js`

4. Initialize in `app.js`

## Accessibility

The UI includes accessibility features:

- **Keyboard Navigation**: All features accessible via keyboard
- **ARIA Labels**: Screen reader support
- **Focus Indicators**: Visible focus states
- **Color Contrast**: WCAG AA compliant
- **Alt Text**: Icons have semantic meaning

## Browser Support

- Chrome/Edge 90+
- Firefox 88+
- Safari 14+
- Mobile browsers (iOS Safari, Chrome Mobile)

## Security Features

- **XSS Prevention**: HTML escaping for user input
- **CSRF Protection**: Token-based authentication
- **Secure Storage**: Tokens in localStorage (consider HttpOnly cookies for production)
- **Input Validation**: Client-side and server-side validation

## Performance

- **Lazy Loading**: Views loaded on demand
- **Debounced Search**: Reduced API calls
- **Efficient Rendering**: Minimal DOM manipulation
- **Compressed Assets**: Minify for production

## Production Deployment

1. **Minify CSS/JS**: Use build tools
2. **Enable HTTPS**: Secure connections only
3. **Configure CORS**: Set proper API CORS headers
4. **Set API URL**: Update `CONFIG.API_BASE_URL`
5. **Cache Static Assets**: Configure web server caching

## Troubleshooting

### Login Issues
- Check API is running
- Verify API URL in config.js
- Check browser console for errors
- Clear localStorage and try again

### Upload Failures
- Check file size (max 100MB default)
- Verify API endpoint is accessible
- Check network tab in browser DevTools
- Ensure proper authentication token

### Theme Not Saving
- Check localStorage is enabled
- Verify no browser extensions blocking storage
- Try incognito mode to test

## Contributing

When adding features:

1. Follow existing code style
2. Add error handling
3. Include loading states
4. Update this documentation
5. Test in multiple browsers
6. Ensure mobile responsiveness

## License

See main project LICENSE file.

## Support

For issues or questions:
- Check browser console for errors
- Review API server logs
- See main project README
- Create GitHub issue with details
