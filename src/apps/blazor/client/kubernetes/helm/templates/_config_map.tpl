{{/* _config_map.tpl */}}
{{- define "maxiar-dotnetstarterkit-blazor.config-map-contents" -}}

ASPNETCORE_ENVIRONMENT: Development
ASPNETCORE_URLS: https://*:443;http://*:80
ASPNETCORE_HTTPS_PORT: 443
ASPNETCORE_Kestrel__Certificates__Default__Password: 
ASPNETCORE_Kestrel__Certificates__Default__Path: 
ApiBaseUrl:  https://dotnetstarterkitwebapi{{ tpl .Values.configmaps.frontend.domainNameCharSeparator . }}{{ tpl .Values.configmaps.frontend.domainName . }}

{{- end }}
