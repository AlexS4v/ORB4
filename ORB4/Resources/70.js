const firstItemBorderRadius = "15px 15px 5px 5px";
const lastItemBorderRadius = "5px 5px 15px 15px";
const uniqueItemBorderRadius = "15px 15px 15px 15px";
const normalItemBorderRadius = "5px 5px 5px 5px";

var currentMenu = undefined;

function createMenuItem(caption, func, borderRadius, marginFix) {
    var menuItem = document.createElement('div');
    menuItem.className = "right-button-menu-item";

    if (marginFix) menuItem.style = `border-radius: ${borderRadius}; margin-top: 5px;`;
    else menuItem.style = `border-radius: ${borderRadius};`;

    menuItem.innerHTML = caption;

    menuItem.onmouseenter = () => { menuItem.className = "right-button-menu-item mouse-e" };
    menuItem.onmousedown = () => { menuItem.className = "right-button-menu-item" };
    menuItem.onmouseup = () => { menuItem.className = "right-button-menu-item mouse-e"; func(); };
    menuItem.onmouseleave = () => { menuItem.className = "right-button-menu-item" };

    return menuItem;
}

function hideCurrentMenu(){
    if (currentMenu !== undefined){
        document.body.removeChild(currentMenu);
        currentMenu = undefined;
    }
}

function rightButtonMenu(items, pos, width) {
    if (currentMenu !== undefined){
        document.body.removeChild(currentMenu);
        currentMenu = undefined;
    }

    var menu = document.createElement('div');
    menu.className = "right-button-menu";
    menu.style = `left: ${pos.x}px; top: ${pos.y}px; width: ${width};`;

    if (items.length == 1) {
        menu.appendChild(
            createMenuItem(items[0].caption, items[0].func, uniqueItemBorderRadius, false)
        );
    } else {
        for (let i = 0; i < items.length; i++) {
            const item = items[i];
            var menuItem;

            if (i == items.length-1)
                menuItem = createMenuItem(item.caption, item.func, lastItemBorderRadius, true);
            else if (i == 0)
                menuItem = createMenuItem(item.caption, item.func, firstItemBorderRadius, false);
            else {
                menuItem = createMenuItem(item.caption, item.func, normalItemBorderRadius, true);
            }

            menu.appendChild(menuItem);
        }
    }

    document.body.appendChild(menu);

    currentMenu = menu;
}