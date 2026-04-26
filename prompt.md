- add a "Links": string array in the json which allows templatable links to be set for the vrchat profile (check api lib for how)
- finish off the sbn update
- add convinience function directly in the scripting so we can use things like date parsing, timespan humanizing, array joining, etc directly in the templates (also add humanizer instead of custom util func)
- add templating for the status aswell with a "Status": string field in the json (check api lib for how)
- use humanizer everywhere else where timespans, etc are shown (like "Waiting 7200000ms for next update..." for example)
- user favorite groups should be accessible like this or similarly easy:
    - {{favorites["<ID>"]name}} -> group name
    - {{favorites["<ID>"]names}} -> user name array
    - {{favorites["<NAME>"]names}} -> user name array
- instead of using a single .sbn file or a single string in the json, make all multiline accepting templates be a list of line objects like this in the json:
```json
{
    "Status": [
        {
            "Content": "{{}}",
            "Compact": "{{}}"
        }
    ],
    "Bio": ...
}
```
where the app will parse that and depending on wether the final string is longer than vrchats limits, instead of simply cutting the full string and adding "..." at the end, first it uses the "Compact" version of the last line (item), if that's still too long, it uses the compact version of the previous line aswell, etc until it fits. if it doesnt fit even when all lines have their compact versions used, then you can start doing the old way of cutting the string and appending "..."