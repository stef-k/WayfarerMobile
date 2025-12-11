# Security

This document describes the security implementation in WayfarerMobile, covering data protection, authentication, and secure storage practices.

## Security Overview

WayfarerMobile implements security at multiple layers:

| Layer | Implementation |
|-------|----------------|
| Transport | HTTPS-only server connections |
| Authentication | Bearer token authentication |
| Storage | SecureStorage for sensitive data |
| App Lock | Optional PIN with salted SHA256 |
| Data | Local SQLite with app sandboxing |

## HTTPS-Only Communication

All server communication uses HTTPS. The app enforces this through:

### URL Validation

```csharp
public bool IsValidServerUrl(string url)
{
    if (string.IsNullOrWhiteSpace(url))
        return false;

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

    // Require HTTPS in production
    #if !DEBUG
    if (uri.Scheme != Uri.UriSchemeHttps)
        return false;
    #endif

    return true;
}
```

### Certificate Pinning (Future Enhancement)

For high-security deployments, certificate pinning can be implemented:

```csharp
// In Android: Implement ICertificatePinning
// In iOS: Use NSURLSessionConfiguration with certificate validation
```

## Authentication

### Bearer Token Authentication

All authenticated API requests include a Bearer token:

```csharp
private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
{
    var request = new HttpRequestMessage(method, $"{_baseUrl}{endpoint}");

    if (!string.IsNullOrEmpty(_settings.ApiToken))
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiToken);
    }

    return request;
}
```

### QR Code Format

The QR code for app configuration contains only:

```json
{
  "serverUrl": "https://your-server.com",
  "apiToken": "your-api-token"
}
```

**Important**: The QR code does NOT contain a user ID. The server determines the user's identity from the API token during authentication. This design means mobile app users do not need email accounts - authentication is entirely token-based.

### Token Lifecycle

| Event | Action |
|-------|--------|
| QR Scan | Token extracted and stored (user ID from server response) |
| API Request | Token attached to Authorization header |
| 401 Response | User prompted to re-authenticate |
| Logout | Token removed from SecureStorage |

### Token Security

- Tokens are never logged
- Tokens are stored in platform-encrypted SecureStorage
- Tokens are not included in crash reports
- Tokens are cleared on logout

## Secure Storage

### MAUI SecureStorage

Sensitive data is stored using MAUI's `SecureStorage` which uses:
- **Android**: EncryptedSharedPreferences (AndroidX Security)
- **iOS**: Keychain Services

### Data Classification

| Classification | Storage Method | Examples |
|----------------|----------------|----------|
| Sensitive | SecureStorage | API token, Server URL |
| Confidential | Preferences | User ID, Email |
| Normal | SQLite | Location queue, Settings |

### Implementation

```csharp
// Store sensitive data
public string? ApiToken
{
    get => SecureStorage.Default.GetAsync(KeyApiToken).GetAwaiter().GetResult();
    set
    {
        if (string.IsNullOrEmpty(value))
        {
            SecureStorage.Default.Remove(KeyApiToken);
        }
        else
        {
            SecureStorage.Default.SetAsync(KeyApiToken, value).GetAwaiter().GetResult();
        }
    }
}

// Store server URL
public string? ServerUrl
{
    get => SecureStorage.Default.GetAsync(KeyServerUrl).GetAwaiter().GetResult();
    set
    {
        if (string.IsNullOrEmpty(value))
        {
            SecureStorage.Default.Remove(KeyServerUrl);
        }
        else
        {
            SecureStorage.Default.SetAsync(KeyServerUrl, value).GetAwaiter().GetResult();
        }
    }
}
```

### Clearing Sensitive Data

```csharp
public void ClearAuth()
{
    SecureStorage.Default.Remove(KeyApiToken);
    SecureStorage.Default.Remove(KeyServerUrl);
    UserId = null;
    UserEmail = null;
}
```

## PIN Lock Security

The app supports optional PIN lock for additional protection.

### Security Model States

```
S0: No PIN configured
    - App accessible without PIN

S1: PIN configured, protection disabled
    - PIN exists but not enforced

S2: PIN configured, protection enabled, session locked
    - PIN required to access app

S3: PIN configured, protection enabled, session unlocked
    - PIN verified, app accessible
```

**Source**: `src/WayfarerMobile/Services/Security/AppLockService.cs`

### PIN Hashing

PINs are hashed using SHA256 with a random salt:

```csharp
private const int SaltLength = 32;

public async Task<bool> SetPinAsync(string pin)
{
    if (!IsValidPin(pin))
        return false;

    // Generate random salt
    byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
    string saltBase64 = Convert.ToBase64String(salt);

    // Hash PIN with salt
    string hash = HashPinWithSalt(pin, salt);

    // Store salt and hash separately
    await SecureStorage.Default.SetAsync(PinSaltKey, saltBase64);
    await SecureStorage.Default.SetAsync(PinHashKey, hash);

    IsPinConfigured = true;
    return true;
}

private static string HashPinWithSalt(string pin, byte[] salt)
{
    byte[] pinBytes = Encoding.UTF8.GetBytes(pin);

    // Combine salt and PIN
    byte[] saltedPin = new byte[salt.Length + pinBytes.Length];
    Buffer.BlockCopy(salt, 0, saltedPin, 0, salt.Length);
    Buffer.BlockCopy(pinBytes, 0, saltedPin, salt.Length, pinBytes.Length);

    // Hash with SHA256
    byte[] hash = SHA256.HashData(saltedPin);
    return Convert.ToBase64String(hash);
}
```

### PIN Verification

```csharp
public async Task<bool> VerifyPinAsync(string pin)
{
    if (!IsValidPin(pin))
        return false;

    string? storedHash = await SecureStorage.Default.GetAsync(PinHashKey);
    if (string.IsNullOrEmpty(storedHash))
        return false;

    // Get salt
    string? saltBase64 = await SecureStorage.Default.GetAsync(PinSaltKey);
    if (string.IsNullOrEmpty(saltBase64))
    {
        // Legacy verification (pre-salt migration)
        return storedHash == HashPinLegacy(pin);
    }

    // Salted verification
    byte[] salt = Convert.FromBase64String(saltBase64);
    string inputHash = HashPinWithSalt(pin, salt);
    return storedHash == inputHash;
}
```

### PIN Validation

```csharp
private const int PinLength = 4;

private static bool IsValidPin(string? pin)
{
    return !string.IsNullOrWhiteSpace(pin) &&
           pin.Length == PinLength &&
           pin.All(char.IsDigit);
}
```

### Session Management

```csharp
public void OnAppToBackground()
{
    // Lock session when app goes to background
    IsSessionUnlocked = false;
    IsPromptAwaiting = false;
}

public async Task<bool> TryUnlockSessionAsync(string pin)
{
    bool verified = await VerifyPinAsync(pin);
    if (verified)
    {
        IsSessionUnlocked = true;
    }
    return verified;
}

public bool IsAccessAllowed()
{
    return !IsProtectionEnabled || IsSessionUnlocked;
}
```

### Legacy PIN Migration

For users upgrading from older versions:

```csharp
private async Task MigrateLegacyStorageAsync()
{
    // Check for old unhashed PIN
    string? oldPin = await SecureStorage.Default.GetAsync(LegacyPinCodeKey);
    if (!string.IsNullOrEmpty(oldPin))
    {
        // Migrate to salted hash
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLength);
        string hash = HashPinWithSalt(oldPin, salt);

        await SecureStorage.Default.SetAsync(PinSaltKey, Convert.ToBase64String(salt));
        await SecureStorage.Default.SetAsync(PinHashKey, hash);

        // Remove old storage
        SecureStorage.Default.Remove(LegacyPinCodeKey);
    }
}
```

## Data Protection

### Location Data

Location data is stored locally in SQLite:
- Queued for server sync
- Purged after successful sync (configurable retention)
- App sandbox prevents access by other apps

### Trip Data

Downloaded trips are stored locally:
- Trip metadata in SQLite
- Map tiles in SQLite (blob storage)
- Encrypted by platform filesystem encryption

### Log Files

Serilog log files:
- Stored in app-private directory
- Automatic rotation (7 days retention)
- No sensitive data (tokens, PINs) logged

```csharp
// Ensure sensitive data is never logged
_logger.LogInformation("API request to {Endpoint}", endpoint);
// NEVER: _logger.LogInformation("Token: {Token}", token);
```

## Platform Security

### Android

**Permissions**:
```xml
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_BACKGROUND_LOCATION" />
<uses-permission android:name="android.permission.INTERNET" />
```

**Foreground Service**:
- Required for background location access
- User-visible notification
- Clear indication of active tracking

**Network Security Config** (optional):
```xml
<!-- res/xml/network_security_config.xml -->
<network-security-config>
    <domain-config cleartextTrafficPermitted="false">
        <domain includeSubdomains="true">your-server.com</domain>
    </domain-config>
</network-security-config>
```

### iOS

**Info.plist Entries**:
```xml
<key>NSLocationWhenInUseUsageDescription</key>
<string>Required for showing your position on the map</string>
<key>NSLocationAlwaysAndWhenInUseUsageDescription</key>
<string>Required for 24/7 location tracking</string>
```

**App Transport Security**:
iOS enforces HTTPS by default. Exceptions require explicit declaration.

## Security Best Practices

### For Developers

1. **Never Log Sensitive Data**
   ```csharp
   // BAD
   _logger.LogDebug("User token: {Token}", token);

   // GOOD
   _logger.LogDebug("User authenticated successfully");
   ```

2. **Use SecureStorage for Secrets**
   ```csharp
   // BAD
   Preferences.Set("api_token", token);

   // GOOD
   await SecureStorage.Default.SetAsync("api_token", token);
   ```

3. **Validate All Input**
   ```csharp
   if (!IsValidServerUrl(url))
       throw new ArgumentException("Invalid server URL");
   ```

4. **Handle Exceptions Safely**
   ```csharp
   catch (Exception ex)
   {
       // Log safely without sensitive context
       _logger.LogError(ex, "Operation failed");
       // Don't expose internal details to user
       await ShowError("An error occurred. Please try again.");
   }
   ```

### For Users

1. Enable PIN lock for sensitive data protection
2. Use a strong, unique PIN
3. Keep the app updated for security patches
4. Review app permissions periodically
5. Use secure network connections (avoid public WiFi)

## Security Testing Checklist

- [ ] Verify HTTPS enforcement
- [ ] Test PIN lock/unlock flow
- [ ] Verify token not in logs
- [ ] Test SecureStorage encryption
- [ ] Verify data cleared on logout
- [ ] Test permission handling
- [ ] Review third-party dependencies for vulnerabilities
- [ ] Test session timeout behavior
- [ ] Verify certificate validation

## Incident Response

If a security vulnerability is discovered:

1. **Do Not** publicly disclose until fixed
2. Contact the security team privately
3. Provide detailed reproduction steps
4. Allow reasonable time for fix deployment

## Next Steps

- [Contributing](16-Contributing.md) - Security requirements for contributions
- [API Integration](13-API.md) - API security details
- [Architecture](11-Architecture.md) - Security architecture overview
