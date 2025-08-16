# Authentication Configuration

This directory contains configuration files for the authentication system.

## Files

### `auth.config.ts`
Contains authentication settings, credentials, and validation logic for development/testing purposes.

## Configuration Options

### Credentials
```typescript
credentials: {
  username: 'admin',
  password: 'admin'
}
```

**Default credentials for development:**
- Username: `admin`
- Password: `admin`

### Validation Rules
```typescript
validation: {
  minUsernameLength: 3,
  minPasswordLength: 1
}
```

### Error Messages
```typescript
messages: {
  invalidCredentials: 'Invalid credentials. Please try again.',
  loginFailed: 'Login failed. Please try again.',
  usernameRequired: 'Username is required',
  passwordRequired: 'Password is required',
  usernameTooShort: 'Username must be at least 3 characters',
  passwordTooShort: 'Password must be at least 1 character'
}
```

## Customization

### Changing Default Credentials
To change the default credentials, update the `credentials` object in `auth.config.ts`:

```typescript
credentials: {
  username: 'your-username',
  password: 'your-password'
}
```

### Modifying Validation Rules
Adjust the minimum length requirements:

```typescript
validation: {
  minUsernameLength: 4,  // Require 4+ characters
  minPasswordLength: 6   // Require 6+ characters
}
```

### Customizing Error Messages
Update the error messages to match your application's tone:

```typescript
messages: {
  invalidCredentials: 'Access denied. Please check your credentials.',
  // ... other messages
}
```

## Production Usage

**Important:** This configuration is for development and testing only. In production:

1. **Remove hardcoded credentials** - Implement proper backend validation
2. **Use secure authentication** - Implement JWT, OAuth, or similar
3. **Add rate limiting** - Prevent brute force attacks
4. **Use HTTPS** - Encrypt all authentication traffic
5. **Implement session management** - Proper logout and token expiration

### Example Production Implementation
```typescript
// Replace the validateCredentials function with:
export async function validateCredentials(username: string, password: string) {
  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    })
    
    if (response.ok) {
      const data = await response.json()
      return { isValid: true, token: data.token }
    } else {
      return { isValid: false, error: 'Invalid credentials' }
    }
  } catch (error) {
    return { isValid: false, error: 'Authentication service unavailable' }
  }
}
```

## Security Notes

- **Never commit real credentials** to version control
- **Use environment variables** for sensitive configuration
- **Implement proper password hashing** in production
- **Add logging** for authentication attempts
- **Consider implementing 2FA** for sensitive applications 