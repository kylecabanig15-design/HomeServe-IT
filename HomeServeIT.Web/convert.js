const fs = require('fs');
let content = fs.readFileSync('/Users/kylechristiancabanig/.gemini/antigravity-ide/brain/21fb3bc8-7c10-4a4e-a09b-d7c77e26b013/.system_generated/steps/352/output.txt', 'utf8');

// Remove React wrappers
content = content.replace(/export default function[^{]+\{\s*return \(\s*/, '');
content = content.replace(/\s*\);\s*\}\s*$/, '');

// Replace className with class
content = content.replace(/className=/g, 'class=');

// Replace {`text`} with text
content = content.replace(/\{`([^`]+)`\}/g, '$1');

// Replace src={imgIcon} with empty or generic paths for now
content = content.replace(/src=\{([^}]+)\}/g, 'src="/img/$1.svg"');

// Write to Views/Home/Index.cshtml
fs.writeFileSync('Views/Home/Index.cshtml', '@{\n    ViewData["Title"] = "Home Page";\n    Layout = "_Layout"; // Or null if you want to include navbar in here directly\n}\n' + content);
console.log("Converted successfully!");
