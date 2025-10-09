# Phase 5 UI/UX - Quick Start Guide

## Overview

Phase 5 delivers a comprehensive, production-ready customer portal with:

✅ **Dark Theme** - Professional dark UI with light theme option  
✅ **Rich Iconography** - Font Awesome 6.4.0 throughout  
✅ **Error Handling** - Toast notifications and validation  
✅ **Interactive UI** - Drag-drop, context menus, real-time updates  
✅ **Responsive Design** - Mobile, tablet, desktop support  
✅ **Complete Features** - File management, search, analytics  

## Quick Start

### 1. Access the UI

Serve the `ui/` directory with any web server:

```bash
# Using Python
cd ui
python3 -m http.server 8080

# Using Node.js
npx http-server ui -p 8080

# Using PHP
php -S localhost:8080 -t ui
```

Then open: `http://localhost:8080`

### 2. Configure API Endpoint

Edit `ui/scripts/config.js`:

```javascript
const CONFIG = {
    API_BASE_URL: 'http://localhost:5289/api',  // Your API endpoint
    // ... other settings
};
```

### 3. First Login

1. Open the UI in your browser
2. Click "Sign Up" tab
3. Enter:
   - Email: `admin@example.com`
   - Username: `admin`
   - Password: `SecurePass123!`
4. Click "Create Account"
5. Switch to "Login" tab
6. Login with your credentials

## Key Features

### Authentication
- Login/signup forms with validation
- Password strength indicator
- Remember me option
- Secure token management

### File Management
- **Upload**: Drag & drop or browse
- **Views**: Grid or list mode
- **Actions**: Download, delete, view info
- **Sorting**: By name, date, or size

### Search
- **Quick Search**: Top bar (auto-debounced)
- **Advanced Search**: Multiple filters
  - Query text
  - File extensions
  - Date range
  - Size range

### Analytics
- Total files count
- Storage usage with quota
- Daily statistics
- File type distribution

### Theme
- **Dark Theme**: Default professional dark
- **Light Theme**: Toggle in top-right
- **Persistent**: Saved to localStorage

## UI Components

### Toast Notifications
Auto-dismissing notifications for:
- ✅ Success messages
- ❌ Error alerts
- ⚠️ Warnings
- ℹ️ Info messages

### Loading States
- Overlay spinner for operations
- Progress bars for uploads
- Contextual loading messages

### Context Menus
Right-click files for quick actions:
- Download
- View info
- Delete

### Responsive Layout
- **Desktop**: Full sidebar + content
- **Tablet**: Collapsible sidebar
- **Mobile**: Bottom navigation

## File Structure

```
ui/
├── index.html              # Main SPA (24KB)
├── README.md               # Full documentation
├── styles/
│   ├── main.css           # Core styles (24KB)
│   └── dark-theme.css     # Dark theme (4KB)
└── scripts/
    ├── config.js          # Configuration
    ├── utils.js           # Utilities (7KB)
    ├── api.js             # API client (8KB)
    ├── auth.js            # Authentication (10KB)
    ├── files.js           # File management (8KB)
    ├── upload.js          # Upload handling (6KB)
    ├── search.js          # Search (6KB)
    ├── analytics.js       # Analytics (4KB)
    └── app.js             # Main app (4KB)
```

## Customization

### Colors
Edit CSS variables in `styles/main.css`:

```css
:root {
    --color-primary: #6366f1;      /* Purple */
    --color-success: #10b981;      /* Green */
    --color-error: #ef4444;        /* Red */
}
```

### Dark Theme Colors
Edit `styles/dark-theme.css`:

```css
.dark-theme {
    --color-bg-primary: #111827;   /* Main background */
    --color-bg-secondary: #1f2937; /* Card background */
}
```

### API Configuration
Edit `scripts/config.js`:

```javascript
const CONFIG = {
    API_BASE_URL: 'http://localhost:5289/api',
    MAX_FILE_SIZE: 100 * 1024 * 1024,  // 100MB
    TOAST_DURATION: 5000,               // 5 seconds
};
```

## Browser Support

- ✅ Chrome/Edge 90+
- ✅ Firefox 88+
- ✅ Safari 14+
- ✅ Mobile browsers

## Production Deployment

1. **Build Assets**
   ```bash
   # Minify CSS/JS (optional)
   npm run build  # If using build tools
   ```

2. **Configure API**
   ```javascript
   // scripts/config.js
   API_BASE_URL: 'https://api.yourapp.com/api'
   ```

3. **Deploy Static Files**
   - Upload `ui/` folder to web server
   - Configure HTTPS
   - Set proper CORS headers on API
   - Enable caching for static assets

4. **Security**
   - Use HTTPS only
   - Set proper CSP headers
   - Configure CORS appropriately
   - Consider using HttpOnly cookies for tokens

## Troubleshooting

### Login Not Working
- Check API is running: `curl http://localhost:5289/health`
- Verify API URL in `config.js`
- Check browser console for errors
- Clear localStorage and try again

### Files Not Loading
- Ensure you're logged in
- Check token in localStorage: `silo_accessToken`
- Verify API endpoints are accessible
- Check network tab in browser DevTools

### Theme Not Saving
- Ensure localStorage is enabled
- Check for browser extensions blocking storage
- Try incognito mode

### Upload Failures
- Check file size (max 100MB)
- Verify API endpoint is correct
- Ensure proper authentication
- Check server logs

## Next Steps

1. **Explore Features**: Try uploading, searching, viewing analytics
2. **Customize Theme**: Edit colors to match your brand
3. **Integrate API**: Connect to your Silo API instance
4. **Deploy**: Follow production deployment steps

## Support

- Full documentation: `ui/README.md`
- API documentation: Main project README
- Create GitHub issue for bugs
- Check browser console for errors

## Phase 6 Preview

Coming next:
- Billing integration (Stripe/PayPal)
- Subscription management
- Compliance automation
- Advanced governance features
