// Authentication configuration
// In production, this would be replaced with proper backend validation

export const authConfig = {
  // Validation settings
  validation: {
    minUsernameLength: 3,
    minPasswordLength: 1
  },
  
  // Error messages
  messages: {
    invalidCredentials: 'Invalid credentials. Please try again.',
    loginFailed: 'Login failed. Please try again.',
    usernameRequired: 'Username is required',
    passwordRequired: 'Password is required',
    usernameTooShort: 'Username must be at least 3 characters',
    passwordTooShort: 'Password must be at least 1 character'
  }
}

// Helper function to validate credentials
export function validateCredentials(username: string, password: string): { isValid: boolean; error?: string } {
  if (!username || username.length < authConfig.validation.minUsernameLength) {
    return { 
      isValid: false, 
      error: username ? authConfig.messages.usernameTooShort : authConfig.messages.usernameRequired 
    }
  }
  
  if (!password || password.length < authConfig.validation.minPasswordLength) {
    return { 
      isValid: false, 
      error: password ? authConfig.messages.passwordTooShort : authConfig.messages.passwordRequired 
    }
  }
  
  // For now, accept any valid username/password combination
  // In production, this would validate against a backend service
  return { isValid: true }
} 