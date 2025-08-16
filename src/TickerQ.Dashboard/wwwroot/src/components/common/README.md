# Authentication Components

This directory contains authentication-related components that have been decoupled from the main layout.

## Components

### AuthHeader.vue
A reusable authentication header component that provides:
- Login form with username/password fields
- User information display when authenticated
- Logout functionality
- Error handling and display

#### Props
- `showLoginForm` (boolean, default: true) - Show/hide the login form
- `showUserInfo` (boolean, default: true) - Show/hide user information
- `showLogout` (boolean, default: true) - Show/hide logout button

#### Events
- `login` - Emitted when login is successful or fails
- `logout` - Emitted when logout occurs

#### Usage
```vue
<AuthHeader
  :show-login-form="true"
  :show-user-info="true"
  :show-logout="true"
  @login="handleLogin"
  @logout="handleLogout"
/>
```

## Architecture

The authentication system follows a layered architecture:

1. **AuthHeader Component** - UI layer for authentication
2. **useAuth Composable** - Business logic and state management
3. **AuthService** - Service layer for authentication operations
4. **AuthStore** - Pinia store for persistent state

### Benefits of this architecture:
- **Separation of Concerns**: Each layer has a specific responsibility
- **Reusability**: Components can be used across different parts of the application
- **Testability**: Each layer can be tested independently
- **Maintainability**: Changes to one layer don't affect others
- **Type Safety**: Full TypeScript support throughout the stack

## Migration from DashboardLayout

The authentication logic has been moved from `DashboardLayout.vue` to these dedicated components. The layout now only handles:
- Navigation
- System status
- System controls (start/stop/restart)

Authentication is handled entirely by the `AuthHeader` component, making the layout cleaner and more focused.

## Pinia Initialization Fix

The authentication system has been designed to handle Pinia initialization gracefully:

- **Lazy Store Access**: The `AuthService` and `useAuth` composable access the store only when methods are called, not during module initialization
- **Error Handling**: All store access is wrapped in try-catch blocks to handle cases where Pinia isn't available yet
- **Safe Fallbacks**: When the store isn't available, the system provides safe default values

This prevents the common error: `"getActivePinia()" was called but there was no active Pinia` that can occur when services are imported before the Vue app is fully initialized. 