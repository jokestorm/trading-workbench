// Header inspection only; accounting and authoritative validation run in .NET.
export function parseCsv(text) {
  text=text.replace(/^\uFEFF/,'');const rows=[];let row=[],cell='',quoted=false,afterQuote=false;
  const finishCell=()=>{row.push(cell);cell='';afterQuote=false;};
  const finishRow=()=>{finishCell();if(row.some(v=>v.trim()))rows.push(row);row=[];};
  for(let i=0;i<text.length;i++) {
    const c=text[i];
    if(quoted){if(c==='"'){if(text[i+1]==='"'){cell+='"';i++;}else{quoted=false;afterQuote=true;}}else cell+=c;}
    else if(c==='"'){if(cell==='=')cell='';if(cell||afterQuote)throw new Error('Unexpected quote in CSV.');quoted=true;}
    else if(c===',')finishCell();
    else if(c==='\r'||c==='\n'){if(c==='\r'&&text[i+1]==='\n')i++;finishRow();}
    else{if(afterQuote&&c.trim())throw new Error('Unexpected text after quoted value.');if(!afterQuote)cell+=c;}
  }
  if(quoted)throw new Error('Unclosed quoted CSV value.');if(cell||row.length||afterQuote)finishRow();return rows;
}
