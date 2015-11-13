
$(document).ready(function() {
    
    // Bind navigation change to click event

    $('#tab-nav-upload, #tab-nav-transform, #tab-nav-statistics').click(function() {

        changeActiveTab($(this).attr('id'));
    });
});

function changeActiveTab(tabId) {

    // For each navigation tab, check if the id matches the click id,
    // and if so set active class, else remove

    $('#tab-nav-upload, #tab-nav-transform, #tab-nav-statistics').each(function() {

        if($(this).attr('id') == tabId) {
            
            $(this).addClass('active-tab');
            
        } else {
            
            $(this).removeClass('active-tab');
        }
    });
    
    // Use replace logic to work out name of corresponding viewport div, and
    // set display property accordingly.

    var viewPortId = tabId.replace('tab-nav-', '') + '-viewport';

    $('#upload-viewport, #transform-viewport, #statistics-viewport').each(function() {

        if($(this).attr('id') == viewPortId) {

            $(this).css('display', 'block');
            
        } else {
            
            $(this).css('display', 'none');
        }
    });
}

