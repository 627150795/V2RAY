# Security policy

## Reporting

Please report security-sensitive issues privately to the repository maintainer instead of opening a public issue. Include a minimal reproduction without real subscription URLs, credentials, node addresses, or configuration databases.

## Security boundaries

- ProxyMonitor reads local proxy-client configuration and stores monitoring history locally.
- v2rayN tests run from a copied configuration snapshot and isolated working directory.
- Node switching always requires user confirmation and a live pre-switch health check.
- Other client integrations remain read-only unless a safe, explicit switching mechanism is implemented.
- The project does not upload subscriptions, nodes, credentials, or monitoring history.

